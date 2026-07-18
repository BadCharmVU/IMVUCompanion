using System;
using System.IO;

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

    /// <summary>
    /// Path for a user-owned config file. Creates the data folder if needed.
    /// Migrates once from next-to-exe or process CWD (legacy relative paths).
    /// </summary>
    public static string GetConfigFile(string fileName)
    {
        Directory.CreateDirectory(Root);
        string dest = Path.Combine(Root, fileName);
        if (File.Exists(dest))
            return dest;

        // Old location: next to the .exe (also wiped by rebuilds / updates)
        TryMigrate(Path.Combine(AppContext.BaseDirectory, fileName), dest);
        if (File.Exists(dest))
            return dest;

        // Legacy: relative path used process working directory
        try
        {
            string cwdPath = Path.GetFullPath(fileName);
            if (!string.Equals(cwdPath, dest, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cwdPath, Path.Combine(AppContext.BaseDirectory, fileName), StringComparison.OrdinalIgnoreCase))
            {
                TryMigrate(cwdPath, dest);
            }
        }
        catch
        {
            // ignore bad CWD
        }

        return dest;
    }

    private static void TryMigrate(string source, string dest)
    {
        try
        {
            if (!File.Exists(source) || File.Exists(dest))
                return;
            File.Copy(source, dest, overwrite: false);
        }
        catch
        {
            // leave dest missing; caller will seed defaults if needed
        }
    }
}
