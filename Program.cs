using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Localizer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
    static void Main(string[] args)
    {
        var rootPath = DetectRootPath();
        var repoDir = ResolveRepoPath(rootPath, GetSetting("LOCALIZER_REPO_DIR", "AutoRetainer"));
        var dictPath = ResolvePath(rootPath, GetSetting("LOCALIZER_DICT_PATH", "zh-TW.json"));
        var sourceDirs = GetSourceDirectories(rootPath, repoDir);

        Console.WriteLine($"[資訊] 當前工作目錄: {Environment.CurrentDirectory}");
        Console.WriteLine($"[資訊] 專案根目錄: {rootPath}");
        Console.WriteLine($"[資訊] 目標倉庫目錄: {repoDir}");
        Console.WriteLine($"[資訊] 字典路徑: {dictPath}");

        if (!Directory.Exists(repoDir))
        {
            Console.WriteLine($"[錯誤] 找不到目標倉庫目錄: {repoDir}");
            Environment.ExitCode = 1;
            return;
        }

        if (sourceDirs.Count == 0)
        {
            Console.WriteLine("[錯誤] 找不到任何可翻譯的來源目錄。請檢查 LOCALIZER_SOURCE_SUBPATHS。");
            Environment.ExitCode = 1;
            return;
        }

        foreach (var dir in sourceDirs)
        {
            Console.WriteLine($"[資訊] 掃描來源目錄: {dir}");
        }

        // 以 JObject 載入字典，分離一般字串翻譯與 _force_translate 節點
        JObject? rawJson = File.Exists(dictPath) ? JObject.Parse(File.ReadAllText(dictPath)) : null;
        var dictionary = rawJson != null
            ? rawJson.Properties()
                .Where(p => p.Value.Type == JTokenType.String)
                .ToDictionary(p => p.Name, p => p.Value.Value<string>() ?? string.Empty)
            : new Dictionary<string, string>();
        var forceTranslate = LoadForceTranslate(rawJson);

        if (forceTranslate.Count > 0)
            Console.WriteLine($"[資訊] 載入 _force_translate：{forceTranslate.Count} 個檔案映射。");

        var files = sourceDirs
            .SelectMany(dir => Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"找到 {files.Length} 個檔案，準備開始掃描...");

        var rewriter = new TranslationRewriter(dictionary, dictPath, forceTranslate);

        foreach (var file in files)
        {
            rewriter.SetCurrentFile(file);
            var code = File.ReadAllText(file);
            SyntaxTree tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            var result = rewriter.Visit(root);

            if (result != root)
            {
                File.WriteAllText(file, result.ToFullString());
                Console.WriteLine($"[已更新] {Path.GetRelativePath(rootPath, file)}");
            }
        }

        var manifestFiles = Directory.GetFiles(repoDir, "*.json", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var file in manifestFiles)
        {
            if (TranslateManifestFile(file, dictionary, dictPath))
            {
                Console.WriteLine($"[已更新] {Path.GetRelativePath(rootPath, file)}");
            }
        }

        Console.WriteLine("正在檢查是否有新發現的字串需寫入字典...");
        rewriter.SaveMissingTranslations();
        Console.WriteLine("中文化處理與字典更新完成！");
    }

    /// <summary>
    /// 從 zh-TW.json 的 _force_translate 節點載入強制翻譯映射。
    /// 結構：{ "相對路徑/檔名.cs": { "英文原文": "繁中翻譯" } }
    /// </summary>
    static Dictionary<string, Dictionary<string, string>> LoadForceTranslate(JObject? json)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (json?["_force_translate"] is not JObject forceNode) return result;

        foreach (var fileProp in forceNode.Properties())
        {
            if (fileProp.Value is not JObject fileNode) continue;
            var fileMap = fileNode.Properties()
                .Where(p => p.Value.Type == JTokenType.String)
                .ToDictionary(p => p.Name, p => p.Value.Value<string>() ?? string.Empty);
            // 空 dict（{}）也要加入：代表「此檔案排除 heuristic 掃描」
            result[fileProp.Name] = fileMap;
        }
        return result;
    }

    static bool TranslateManifestFile(string filePath, Dictionary<string, string> dictionary, string dictPath)
    {
        try
        {
            var json = JObject.Parse(File.ReadAllText(filePath));
            var changed = false;

            foreach (var propertyName in new[] { "Punchline", "Description" })
            {
                if (json[propertyName]?.Type != JTokenType.String)
                {
                    continue;
                }

                var original = json[propertyName]!.Value<string>()!;
                if (string.IsNullOrWhiteSpace(original))
                {
                    continue;
                }

                if (dictionary.TryGetValue(original, out var translated) && !string.IsNullOrWhiteSpace(translated) && translated != original)
                {
                    json[propertyName] = translated;
                    changed = true;
                    continue;
                }

                if (!dictionary.ContainsKey(original))
                {
                    dictionary[original] = original;
                    SaveDictionary(dictPath, dictionary);
                }
            }

            if (changed)
            {
                File.WriteAllText(filePath, json.ToString(Formatting.Indented));
            }

            return changed;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    static void SaveDictionary(string dictPath, Dictionary<string, string> dictionary)
    {
        File.WriteAllText(dictPath, JsonConvert.SerializeObject(dictionary, Formatting.Indented));
    }

    static string DetectRootPath()
    {
        var current = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(current, "DalamudModLocalizer.csproj")))
        {
            return current;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
    }

    static string GetSetting(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    static string ResolvePath(string rootPath, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        // Consumer-provided relative paths such as the translation dictionary
        // should resolve from the caller's working directory before falling back
        // to the template repository root.
        var cwdCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
        if (File.Exists(cwdCandidate) || Directory.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        return Path.GetFullPath(Path.Combine(rootPath, path));
    }

    static string ResolveRepoPath(string rootPath, string path)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        var cwdCandidate = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, path));
        if (Directory.Exists(cwdCandidate))
        {
            return cwdCandidate;
        }

        return Path.GetFullPath(Path.Combine(rootPath, path));
    }

    static List<string> GetSourceDirectories(string rootPath, string repoDir)
    {
        var setting = GetSetting("LOCALIZER_SOURCE_SUBPATHS", "AutoRetainer");
        var separators = new[] { ';', '\n' };
        return setting
            .Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(repoDir, path)))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
