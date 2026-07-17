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
            return v ?? new Version(0, 9, 0);
        }
    }

    public static string ShortLabel => $"v{Current.Major}.{Current.Minor}";

    public static string FullLabel => $"v{Current.Major}.{Current.Minor}.{Current.Build}";

    public static string WindowTitle => $"IMVU Companion {ShortLabel}";
}