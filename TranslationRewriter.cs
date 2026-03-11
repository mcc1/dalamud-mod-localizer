using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Localizer
{
    public class TranslationRewriter : CSharpSyntaxRewriter
    {
        private readonly Dictionary<string, string> _dictionary;
        private readonly HashSet<string> _knownTexts;
        private readonly string _jsonPath;
        // 使用 HashSet 確保單次運行中，同一個新字串只會被記錄一次
        public HashSet<string> MissingTranslations { get; } = new HashSet<string>();

        private readonly string[] _uiKeywords = { "Text", "Button", "Label", "Combo", "Header", "Section", "Tooltip", "MenuItem", "Checkbox", "Help", "Notify", "Info", "FormatToken", "InputInt", "Widget", "EnumCombo", "Selectable", "CollapsingHeader" };
        private readonly string[] _blackList = { "PushID", "GetConfig", "Log", "Debug", "Print", "ExecuteCommand", "ToString", "GetField", "GetProperty", "SetFilter", "Tag", "GetTag", "InternalName", "Database", "HasTag", "AddTag", "Find" };

        public TranslationRewriter(Dictionary<string, string> dictionary, string jsonPath)
        {
            _dictionary = dictionary;
            _jsonPath = jsonPath;
            _knownTexts = BuildKnownTexts(dictionary);
        }

        // --- 核心翻譯與記錄邏輯 ---

        private bool TryGetTranslation(string original, out string translated)
        {
            if (string.IsNullOrWhiteSpace(original))
            {
                translated = null;
                return false;
            }

            // 嘗試三種匹配模式
            if (_dictionary.TryGetValue(original, out translated) ||
                _dictionary.TryGetValue(original.Trim(), out translated) ||
                _dictionary.TryGetValue(NormalizeKey(original), out translated))
            {
                translated = DecodeTranslationEscapes(translated);
                return true;
            }

            // If the current source already contains a translated string, do not re-add it as a new key.
            if (_knownTexts.Contains(original) ||
                _knownTexts.Contains(original.Trim()) ||
                _knownTexts.Contains(NormalizeKey(original)))
            {
                translated = original;
                return false;
            }

            // 若找不到翻譯，記錄到缺失清單
            if (!_dictionary.ContainsKey(original))
            {
                MissingTranslations.Add(original);
            }
            return false;
        }

        /// <summary>
        /// 將所有新發現的字串寫回 JSON 檔案
        /// </summary>
        public void SaveMissingTranslations()
        {
            if (MissingTranslations.Count == 0) return;

            try
            {
                JObject json;
                if (File.Exists(_jsonPath))
                {
                    string content = File.ReadAllText(_jsonPath);
                    json = JObject.Parse(content);
                }
                else
                {
                    json = new JObject();
                }

                bool added = false;
                foreach (var key in MissingTranslations)
                {
                    if (json[key] == null)
                    {
                        // 預設將 Value 設為 Key，方便後續搜尋與翻譯
                        json[key] = key;
                        added = true;
                    }
                }

                if (added)
                {
                    // 使用 Indented 格式讓 JSON 易於閱讀
                    File.WriteAllText(_jsonPath, json.ToString(Formatting.Indented), Encoding.UTF8);
                    Console.WriteLine($"[INFO] 已將 {MissingTranslations.Count} 個新字串寫入 {_jsonPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 儲存 JSON 時發生錯誤: {ex.Message}");
            }
        }

        // --- Roslyn 節點訪問覆寫 ---

        public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
        {
            var sb = new StringBuilder();
            int placeholderIndex = 0;
            var interpolations = new List<InterpolationSyntax>();

            foreach (var content in node.Contents)
            {
                if (content is InterpolatedStringTextSyntax text)
                    sb.Append(text.TextToken.ValueText);
                else if (content is InterpolationSyntax interp)
                {
                    sb.Append($"{{{placeholderIndex++}}}");
                    interpolations.Add(interp);
                }
            }

            string templateKey = sb.ToString();

            if (ShouldTranslate(node, templateKey))
            {
                if (TryGetTranslation(templateKey, out var translated))
                {
                    return ReconstructInterpolatedString(node, translated, interpolations);
                }
            }

            return base.VisitInterpolatedStringExpression(node);
        }

        public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
        {
            if (node.IsKind(SyntaxKind.StringLiteralExpression))
            {
                string originalText = NormalizeLiteralText(node.Token.ValueText);
                if (ShouldTranslate(node, originalText))
                {
                    if (TryGetTranslation(originalText, out var translated))
                    {
                        return SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal(translated))
                            // .WithLeadingTrivia(node.GetLeadingTrivia())
                            .WithTrailingTrivia(node.GetTrailingTrivia());
                    }
                }
            }
            return base.VisitLiteralExpression(node);
        }

        // --- 輔助判斷方法 ---

        private bool ShouldTranslate(SyntaxNode node, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            if (text.StartsWith("##") || text.StartsWith("Component") || text.StartsWith("\\u")) return false;
            if (LooksLikeNonTranslatableText(text)) return false;
            var invocations = node.Ancestors().OfType<InvocationExpressionSyntax>().ToList();

            foreach (var invocation in invocations)
            {
                string methodName = GetMethodName(invocation);
                if (_blackList.Any(b => methodName.Equals(b, StringComparison.OrdinalIgnoreCase))) return false;
            }

            if (IsCommandHelpArgument(node)) return true;
            if (IsHelpMessageAssignment(node)) return true;
            if (IsMenuItemText(node)) return true;

            foreach (var invocation in invocations)
            {
                string methodName = GetMethodName(invocation);
                if (_uiKeywords.Any(k => methodName.Contains(k, StringComparison.OrdinalIgnoreCase))) return true;
                if (methodName.Equals("SetTooltip", StringComparison.OrdinalIgnoreCase)) return true;
                if (methodName.Equals("BeginCombo", StringComparison.OrdinalIgnoreCase)) return true;
                if (methodName.Equals("SmallButton", StringComparison.OrdinalIgnoreCase)) return true;
            }

            // 檢查方法定義名稱
            var methodDecl = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDecl != null)
            {
                string methodName = methodDecl.Identifier.Text;
                if (methodName.Contains("Format") || methodName.Contains("Get") ||
                    methodName.Contains("Draw") || methodName.Contains("Tooltip") ||
                    methodName.Contains("ToString"))
                {
                    return IsHumanText(text);
                }
            }

            // 檢查屬性名稱
            var property = node.Ancestors().OfType<PropertyDeclarationSyntax>().FirstOrDefault();
            if (property != null)
            {
                string propName = property.Identifier.Text;
                if (propName == "Name" || propName == "Path") return true;
            }

            return false;
        }

        private bool IsCommandHelpArgument(SyntaxNode node)
        {
            var argument = node.AncestorsAndSelf().OfType<ArgumentSyntax>().FirstOrDefault();
            if (argument?.Parent is not ArgumentListSyntax argumentList)
            {
                return false;
            }

            if (argumentList.Parent is not InvocationExpressionSyntax invocation)
            {
                return false;
            }

            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (!memberAccess.Expression.ToString().EndsWith("EzCmd", StringComparison.Ordinal))
            {
                return false;
            }

            if (!memberAccess.Name.Identifier.Text.Equals("Add", StringComparison.Ordinal))
            {
                return false;
            }

            var index = argumentList.Arguments.IndexOf(argument);
            return index >= 2;
        }

        private bool IsHelpMessageAssignment(SyntaxNode node)
        {
            var assignment = node.AncestorsAndSelf().OfType<AssignmentExpressionSyntax>().FirstOrDefault();
            if (assignment?.Left is IdentifierNameSyntax identifier)
            {
                return identifier.Identifier.Text.Equals("HelpMessage", StringComparison.Ordinal);
            }

            var initializer = node.AncestorsAndSelf().OfType<InitializerExpressionSyntax>().FirstOrDefault();
            if (initializer == null)
            {
                return false;
            }

            return initializer.Expressions
                .OfType<AssignmentExpressionSyntax>()
                .Any(x => x.Right == node && x.Left is IdentifierNameSyntax name &&
                          name.Identifier.Text.Equals("HelpMessage", StringComparison.Ordinal));
        }

        private bool IsMenuItemText(SyntaxNode node)
        {
            var invocations = node.Ancestors().OfType<InvocationExpressionSyntax>();
            var usesMenuTextBuilder = invocations.Any(x =>
            {
                var methodName = GetMethodName(x);
                return methodName.Equals("AddText", StringComparison.Ordinal) ||
                       methodName.Equals("AddUiForeground", StringComparison.Ordinal);
            });

            if (!usesMenuTextBuilder)
            {
                return false;
            }

            return node.Ancestors().OfType<ObjectCreationExpressionSyntax>().Any(x =>
                x.Type.ToString().EndsWith("MenuItem", StringComparison.Ordinal));
        }

        private bool IsHumanText(string text)
        {
            if (LooksLikeNonTranslatableText(text)) return false;

            string clean = text.Trim(' ', '\n', '\r', '\"', '\\', 't', '#', '_'); // 增加過濾符號
            if (clean.Length <= 1) return false; // 太短的通常是代號或符號        
            if (char.IsLower(clean[0])) return false; // 過濾字首小寫
            // 判斷是否含有字母或中文字元
            return clean.Any(c => char.IsLetter(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.OtherLetter);
            
        }

        private string NormalizeKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private bool LooksLikeNonTranslatableText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }

            var normalized = text.Trim();
            if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Regex.IsMatch(normalized, @"(^|[\s""'`])[\w./\\-]+\.(png|jpg|jpeg|svg|webp|dds|tex)\b", RegexOptions.IgnoreCase);
        }

        private string NormalizeLiteralText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            if (!normalized.Contains('\n'))
            {
                return normalized;
            }

            var lines = normalized.Split('\n');
            var firstNonEmpty = 0;
            while (firstNonEmpty < lines.Length && string.IsNullOrWhiteSpace(lines[firstNonEmpty]))
            {
                firstNonEmpty++;
            }

            var lastNonEmpty = lines.Length - 1;
            while (lastNonEmpty >= firstNonEmpty && string.IsNullOrWhiteSpace(lines[lastNonEmpty]))
            {
                lastNonEmpty--;
            }

            if (firstNonEmpty > lastNonEmpty)
            {
                return string.Empty;
            }

            var contentLines = lines[firstNonEmpty..(lastNonEmpty + 1)];
            var minIndent = contentLines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(GetLeadingWhitespaceCount)
                .DefaultIfEmpty(0)
                .Min();

            for (var i = 0; i < contentLines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(contentLines[i]) && contentLines[i].Length >= minIndent)
                {
                    contentLines[i] = contentLines[i][minIndent..];
                }
            }

            return string.Join("\n", contentLines);
        }

        private string DecodeTranslationEscapes(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return text
                .Replace("\\r\\n", "\r\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }

        private int GetLeadingWhitespaceCount(string text)
        {
            var count = 0;
            while (count < text.Length && char.IsWhiteSpace(text[count]))
            {
                count++;
            }
            return count;
        }

        private HashSet<string> BuildKnownTexts(Dictionary<string, string> dictionary)
        {
            var knownTexts = new HashSet<string>();
            foreach (var pair in dictionary)
            {
                AddKnownText(knownTexts, pair.Key);
                AddKnownText(knownTexts, pair.Value);
            }
            return knownTexts;
        }

        private void AddKnownText(HashSet<string> knownTexts, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            knownTexts.Add(text);
            knownTexts.Add(text.Trim());
            knownTexts.Add(NormalizeKey(text));
        }

        private string GetMethodName(InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax m) return m.Name.Identifier.Text;
            if (invocation.Expression is IdentifierNameSyntax i) return i.Identifier.Text;
            return invocation.Expression.ToString();
        }

        private SyntaxNode ReconstructInterpolatedString(InterpolatedStringExpressionSyntax node, string translatedTemplate, List<InterpolationSyntax> interpolations)
        {
            var contents = new List<InterpolatedStringContentSyntax>();
            bool isRawString = node.StringStartToken.ValueText.StartsWith("$" + "\"\"\"");
            string leadingWhitespace = "";
            if (isRawString)
            {
                var endLocation = node.StringEndToken.GetLocation().GetLineSpan();
                int lineIndex = endLocation.StartLinePosition.Line;
                
                var sourceText = node.SyntaxTree.GetText();
                string wholeLineText = sourceText.Lines[lineIndex].ToString();
                
                leadingWhitespace = new string(wholeLineText.TakeWhile(char.IsWhiteSpace).ToArray());
            }

            var matches = Regex.Matches(translatedTemplate, @"\{(\d+)\}");
            int lastIndex = 0;

            string ProcessText(string rawText, bool isFirstPart)
            {
                if (string.IsNullOrEmpty(rawText)) return rawText;
                if (!isRawString)
                {
                    // For normal interpolated strings ($"..."), escape control chars to keep valid C# syntax.
                    return rawText
                        .Replace("\\", "\\\\")
                        .Replace("\"", "\\\"")
                        .Replace("\r", "\\r")
                        .Replace("\n", "\\n")
                        .Replace("\t", "\\t");
                }

                var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = lines[i].TrimStart();
                }

                string joined = string.Join("\n" + leadingWhitespace, lines);
                return isFirstPart ? leadingWhitespace + joined : joined;
            }

            bool isFirst = true;

            foreach (Match match in matches)
            {
                if (match.Index > lastIndex)
                {
                    string textPart = translatedTemplate.Substring(lastIndex, match.Index - lastIndex);
                    string finalPart = ProcessText(textPart, isFirst);
                    isFirst = false;

                    contents.Add(SyntaxFactory.InterpolatedStringText(
                        SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.InterpolatedStringTextToken, finalPart, textPart, SyntaxFactory.TriviaList())));
                }

                int idx = int.Parse(match.Groups[1].Value);
                if (idx < interpolations.Count) contents.Add(interpolations[idx]);
                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < translatedTemplate.Length)
            {
                string rest = translatedTemplate.Substring(lastIndex);
                string finalRest = ProcessText(rest, isFirst);

                contents.Add(SyntaxFactory.InterpolatedStringText(
                    SyntaxFactory.Token(SyntaxFactory.TriviaList(), SyntaxKind.InterpolatedStringTextToken, finalRest, rest, SyntaxFactory.TriviaList())));
            }

            return SyntaxFactory.InterpolatedStringExpression(
                node.StringStartToken,
                SyntaxFactory.List(contents),
                node.StringEndToken)
                .WithLeadingTrivia(node.GetLeadingTrivia())   // 前導空格
                .WithTrailingTrivia(node.GetTrailingTrivia()); // 後繼空格
        }
    }
}
