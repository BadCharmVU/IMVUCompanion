using System;
using System.IO;
using System.Reflection;

namespace IMVUCompanion;

internal static class AppVersion
{
    public const string VersionCheckUrl =
        "https://gist.githubusercontent.com/BadCharmVU/d510193765f2062f315d65de91bbceec/raw/version.json";

    public const string ReleasesApiUrl =
        "https://api.github.com/repos/BadCharmVU/IMVUCompanion/releases/latest";

    /// <summary>
    /// True when running the daily dev build under the repo bin\Release\... path
    /// (or any non-installer location). False only for a normal installed app.
    /// </summary>
    public static bool IsDevBuild
    {
        get
        {
            try
            {
                string dir = (AppContext.BaseDirectory ?? "").Replace('/', '\\').TrimEnd('\\');
                if (string.IsNullOrEmpty(dir))
                    return true;

                // Installed by Setup (Inno): LocalAppData\Programs\IMVU Companion or Program Files
                if (dir.Contains(@"\Programs\IMVU Companion", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (dir.Contains(@"\Program Files\IMVU Companion", StringComparison.OrdinalIgnoreCase))
                    return false;
                if (dir.Contains(@"\Program Files (x86)\IMVU Companion", StringComparison.OrdinalIgnoreCase))
                    return false;

                // Explicit: repo framework build path is always dev
                if (dir.Contains(@"\bin\Release\", StringComparison.OrdinalIgnoreCase) ||
                    dir.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase))
                    return true;

                // publish\ folders are shipping artifacts, not your daily test exe — treat as dev (no auto-update)
                if (dir.Contains(@"\publish", StringComparison.OrdinalIgnoreCase))
                    return true;

                return true;
            }
            catch
            {
                return true;
            }
        }
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v ?? new Version(0, 9, 5);
        }
    }

    /// <summary>v0.9 or v0.9.2 — always includes patch when non-zero so updates are visible.</summary>
    public static string FormatLabel(Version? v)
    {
        if (v == null) return "v?";
        // System.Version uses Major.Minor.Build.Revision; Build is the third segment (patch).
        if (v.Build > 0)
            return $"v{v.Major}.{v.Minor}.{v.Build}";
        return $"v{v.Major}.{v.Minor}";
    }

    public static string ShortLabel => FormatLabel(Current);

    public static string FullLabel => FormatLabel(Current);

    public static string WindowTitle =>
        IsDevBuild ? $"IMVU Companion {ShortLabel} (dev)" : $"IMVU Companion {ShortLabel}";
}
