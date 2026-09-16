using System;
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
            try
            {
                string? info = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                    ?.InformationalVersion;
                if (!string.IsNullOrWhiteSpace(info))
                {
                    string ver = info.Split('+', '-')[0].Trim();
                    if (Version.TryParse(ver, out Version? parsed) && parsed != null)
                        return Normalize(parsed);
                }
            }
            catch { }

            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return Normalize(v ?? new Version(1, 0, 0));
        }
    }

    /// <summary>
    /// Pad unspecified Build/Revision to 0 so gist "1.0.0" equals assembly 1.0.0.0.
    /// </summary>
    public static Version Normalize(Version v) =>
        new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    public static bool IsNewer(Version? remote, Version? local) =>
        remote != null && (local == null || Normalize(remote) > Normalize(local));

    /// <summary>Always three segments so v1.0.0 is visible, not "v1.0".</summary>
    public static string FormatLabel(Version? v)
    {
        if (v == null) return "v?";
        v = Normalize(v);
        return $"v{v.Major}.{v.Minor}.{v.Build}";
    }

    public static string ShortLabel => FormatLabel(Current);

    public static string FullLabel => FormatLabel(Current);

    public static string WindowTitle =>
        IsDevBuild ? $"IMVU Companion {ShortLabel} (dev)" : $"IMVU Companion {ShortLabel}";
}
