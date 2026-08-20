using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IMVUCompanion;

/// <summary>
/// Stable user config location under %LOCALAPPDATA%\IMVUCompanion.
/// Survives app restarts, rebuilds, and version updates (installer never touches this folder).
/// </summary>
internal static class UserDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IMVUCompanion");

    /// <summary>Single SQLite file for welcome, triggers, console, recorder, layout, and prefs.</summary>
    public static string DatabaseFile
    {
        get
        {
            Directory.CreateDirectory(Root);
            return Path.Combine(Root, "companion.db");
        }
    }

    /// <summary>
    /// Path for a leftover/legacy file under the data folder. Does not copy from the exe directory.
    /// </summary>
    public static string GetConfigFile(string fileName)
    {
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, fileName);
    }

    public static string LangFile(string stem, string lang)
    {
        string code = (lang ?? "en").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code)) code = "en";
        return GetConfigFile(stem + "-" + code + ".json");
    }

    public static List<string> ListLangCodes(string stem)
    {
        var codes = new List<string>();
        try
        {
            Directory.CreateDirectory(Root);
            string prefix = stem + "-";
            foreach (string path in Directory.GetFiles(Root, stem + "-*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(path) ?? "";
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string code = name[prefix.Length..].Trim().ToLowerInvariant();
                if (code.Length > 0 && !codes.Contains(code, StringComparer.OrdinalIgnoreCase))
                    codes.Add(code);
            }
        }
        catch { }
        return codes;
    }

    public static void WriteAtomic(string path, string json)
    {
        Directory.CreateDirectory(Root);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, path, overwrite: true);
        try { File.Delete(tmp); } catch { }
    }
}
