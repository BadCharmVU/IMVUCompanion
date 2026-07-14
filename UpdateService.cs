using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IMVUCompanion;

internal sealed class UpdateCheckResult
{
    public bool UpdateAvailable { get; init; }
    public Version? RemoteVersion { get; init; }
    public string DownloadUrl { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string? Error { get; init; }
}

internal static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IMVUCompanion-Updater/1.0");
        return client;
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            var fromManifest = await TryReadVersionManifestAsync(ct);
            if (fromManifest != null)
                return Compare(fromManifest.Value.version, fromManifest.Value.url, fromManifest.Value.notes);

            var fromRelease = await TryReadLatestReleaseAsync(ct);
            if (fromRelease != null)
                return Compare(fromRelease.Value.version, fromRelease.Value.url, fromRelease.Value.notes);

            return new UpdateCheckResult { Error = "Could not reach update server" };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Error = ex.Message };
        }
    }

    private static UpdateCheckResult Compare(Version remote, string url, string notes)
    {
        bool available = remote > AppVersion.Current;
        return new UpdateCheckResult
        {
            UpdateAvailable = available,
            RemoteVersion = remote,
            DownloadUrl = url ?? "",
            ReleaseNotes = notes ?? "",
        };
    }

    private static async Task<(Version version, string url, string notes)?> TryReadVersionManifestAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(AppVersion.VersionCheckUrl, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        if (!root.TryGetProperty("version", out var verEl)) return null;

        string verText = verEl.GetString() ?? "";
        if (!Version.TryParse(NormalizeVersion(verText), out Version? remote) || remote == null)
            return null;

        string url = root.TryGetProperty("downloadUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
        string notes = root.TryGetProperty("releaseNotes", out var notesEl) ? notesEl.GetString() ?? "" : "";
        return (remote, url, notes);
    }

    private static async Task<(Version version, string url, string notes)?> TryReadLatestReleaseAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(AppVersion.ReleasesApiUrl, ct);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;

        string tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        tag = tag.TrimStart('v', 'V');
        if (!Version.TryParse(NormalizeVersion(tag), out Version? remote) || remote == null)
            return null;

        string notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
        string url = "";
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                url = asset.TryGetProperty("browser_download_url", out var dlEl) ? dlEl.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(url)) break;
            }
        }

        if (string.IsNullOrEmpty(url) && root.TryGetProperty("html_url", out var htmlEl))
            url = htmlEl.GetString() ?? "";

        return (remote, url, notes);
    }

    private static string NormalizeVersion(string text)
    {
        text = (text ?? "").Trim().TrimStart('v', 'V');
        var parts = text.Split('.');
        if (parts.Length == 1) return parts[0] + ".0.0";
        if (parts.Length == 2) return parts[0] + "." + parts[1] + ".0";
        return text;
    }

    public static async Task<string?> DownloadUpdateAsync(string downloadUrl, IProgress<string>? log, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            return null;

        string tempDir = Path.Combine(Path.GetTempPath(), "IMVUCompanion_update");
        Directory.CreateDirectory(tempDir);

        string fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = "IMVUCompanion_download.bin";
        string dest = Path.Combine(tempDir, fileName);

        log?.Report("Downloading update…");
        using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = File.Create(dest))
            await input.CopyToAsync(output, ct);

        if (dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("Extracting update package…");
            string extractDir = Path.Combine(tempDir, "extracted");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(dest, extractDir);

            string? exe = FindExe(extractDir);
            return exe;
        }

        if (dest.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return dest;

        return dest;
    }

    private static string? FindExe(string dir)
    {
        string direct = Path.Combine(dir, "IMVUCompanion.exe");
        if (File.Exists(direct)) return direct;
        foreach (var file in Directory.EnumerateFiles(dir, "IMVUCompanion.exe", SearchOption.AllDirectories))
            return file;
        foreach (var file in Directory.EnumerateFiles(dir, "*.exe", SearchOption.AllDirectories))
            return file;
        return null;
    }

    public static bool TryScheduleApplyAndRestart(string newExePath, out string? error)
    {
        error = null;
        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = "Updates apply only to the installed .exe (not dotnet run).";
            return false;
        }

        if (!File.Exists(newExePath))
        {
            error = "Downloaded update file is missing.";
            return false;
        }

        string tempDir = Path.GetDirectoryName(newExePath) ?? Path.GetTempPath();
        string staged = Path.Combine(tempDir, "IMVUCompanion_new.exe");
        File.Copy(newExePath, staged, true);

        int pid = Environment.ProcessId;
        string script = Path.Combine(tempDir, "imvu_apply_update.cmd");
        File.WriteAllText(script, $"""
@echo off
setlocal
:waitloop
tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul
if %ERRORLEVEL%==0 (
  timeout /t 1 /nobreak >nul
  goto waitloop
)
copy /Y "{staged}" "{currentExe}"
if %ERRORLEVEL% NEQ 0 exit /b 1
start "" "{currentExe}"
del "{staged}" 2>nul
del "%~f0"
""");

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = script,
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
        });

        return true;
    }
}