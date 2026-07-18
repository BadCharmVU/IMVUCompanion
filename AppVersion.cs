using System;
using System.Reflection;

namespace IMVUCompanion;

internal static class AppVersion
{
    public const string VersionCheckUrl =
        "https://gist.githubusercontent.com/BadCharmVU/d510193765f2062f315d65de91bbceec/raw/version.json";

    public const string ReleasesApiUrl =
        "https://api.github.com/repos/BadCharmVU/IMVUCompanion/releases/latest";

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v ?? new Version(0, 9, 2);
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

    public static string WindowTitle => $"IMVU Companion {ShortLabel}";
}