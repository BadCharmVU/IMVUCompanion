using System.Reflection;

namespace IMVUCompanion;

internal static class AppVersion
{
    public const string VersionCheckUrl =
        "https://raw.githubusercontent.com/BadCharmVU/IMVUCompanion/main/version.json";

    public const string ReleasesApiUrl =
        "https://api.github.com/repos/BadCharmVU/IMVUCompanion/releases/latest";

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v ?? new Version(0, 7, 1);
        }
    }

    public static string ShortLabel => $"v{Current.Major}.{Current.Minor}";

    public static string FullLabel => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

    public static string WindowTitle => $"IMVU Companion {ShortLabel}";
}