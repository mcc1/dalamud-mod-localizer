using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Localizer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;

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

        var dictionary = File.Exists(dictPath)
            ? JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(dictPath))
            : new Dictionary<string, string>();

        var files = sourceDirs
            .SelectMany(dir => Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Console.WriteLine($"找到 {files.Length} 個檔案，準備開始掃描...");

        var rewriter = new TranslationRewriter(dictionary ?? new(), dictPath);

        foreach (var file in files)
        {
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

        Console.WriteLine("正在檢查是否有新發現的字串需寫入字典...");
        rewriter.SaveMissingTranslations();
        Console.WriteLine("中文化處理與字典更新完成！");
    }

    static string DetectRootPath()
    {
        var current = Environment.CurrentDirectory;
        if (File.Exists(Path.Combine(current, "AutoRetainerLocalizer.csproj")))
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
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(rootPath, path));
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
