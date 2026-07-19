using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IMVUCompanion;

internal sealed class UpdateCheckResult
{
    /// <summary>True when any source reports a version newer than the running app (UI).</summary>
    public bool UpdateAvailable { get; init; }

    /// <summary>
    /// True only when the gist manifest has a matching newer version, https downloadUrl, and valid sha256.
    /// Download/apply must not proceed without this.
    /// </summary>
    public bool CanDownload { get; init; }

    public Version? RemoteVersion { get; init; }
    public string DownloadUrl { get; init; } = "";
    /// <summary>Lowercase hex SHA-256 of the file at DownloadUrl (from manifest only).</summary>
    public string Sha256 { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string? Error { get; init; }
}

internal sealed class DownloadUpdateResult
{
    /// <summary>Path to the verified installer/exe ready to apply, or null on failure.</summary>
    public string? Path { get; init; }
    public string? Error { get; init; }
}

internal static class UpdateService
{
    private const string ExpectedAppExeName = "IMVUCompanion.exe";
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IMVUCompanion-Updater/1.0");
        return client;
    }

    public static async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken ct = default)
    {
        try
        {
            ManifestInfo? manifest = null;
            ReleaseInfo? release = null;
            string? lastError = null;

            try
            {
                manifest = await TryReadVersionManifestAsync(ct);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            try
            {
                release = await TryReadLatestReleaseAsync(ct);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }

            if (manifest == null && release == null)
                return new UpdateCheckResult { Error = lastError ?? "Could not reach update server" };

            // Display version = max(manifest, release) for availability UI
            Version? displayVersion = null;
            string notes = "";
            if (manifest != null)
            {
                displayVersion = manifest.Version;
                notes = manifest.Notes;
            }
            if (release != null && (displayVersion == null || release.Version > displayVersion))
            {
                displayVersion = release.Version;
                notes = string.IsNullOrWhiteSpace(release.Notes) ? notes : release.Notes;
            }

            bool updateAvailable = displayVersion != null && displayVersion > AppVersion.Current;

            // Download/apply: ONLY from gist manifest — https URL + sha256 required. Strict, no exceptions.
            bool canDownload = false;
            string downloadUrl = "";
            string sha256 = "";
            if (manifest != null &&
                manifest.Version > AppVersion.Current &&
                IsHttpsUrl(manifest.DownloadUrl) &&
                IsValidSha256Hex(manifest.Sha256))
            {
                canDownload = true;
                downloadUrl = manifest.DownloadUrl;
                sha256 = NormalizeSha256(manifest.Sha256);
                if (!string.IsNullOrWhiteSpace(manifest.Notes))
                    notes = manifest.Notes;
            }

            return new UpdateCheckResult
            {
                UpdateAvailable = updateAvailable,
                CanDownload = canDownload,
                RemoteVersion = displayVersion,
                DownloadUrl = downloadUrl,
                Sha256 = sha256,
                ReleaseNotes = notes ?? "",
                Error = lastError
            };
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult { Error = ex.Message };
        }
    }

    private sealed class ManifestInfo
    {
        public required Version Version { get; init; }
        public required string DownloadUrl { get; init; }
        public required string Sha256 { get; init; }
        public string Notes { get; init; } = "";
    }

    private sealed class ReleaseInfo
    {
        public required Version Version { get; init; }
        public string Notes { get; init; } = "";
    }

    private static async Task<ManifestInfo?> TryReadVersionManifestAsync(CancellationToken ct)
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
        string sha = root.TryGetProperty("sha256", out var shaEl) ? shaEl.GetString() ?? "" : "";

        // Missing/malformed sha256 or downloadUrl still returns version for "update available" display,
        // but CheckForUpdateAsync only sets CanDownload when both are valid.
        return new ManifestInfo
        {
            Version = remote,
            DownloadUrl = url.Trim(),
            Sha256 = sha.Trim(),
            Notes = notes
        };
    }

    private static async Task<ReleaseInfo?> TryReadLatestReleaseAsync(CancellationToken ct)
    {
        // Availability only — never used as integrity / download authority.
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
        return new ReleaseInfo { Version = remote, Notes = notes };
    }

    private static string NormalizeVersion(string text)
    {
        text = (text ?? "").Trim().TrimStart('v', 'V');
        var parts = text.Split('.');
        if (parts.Length == 1) return parts[0] + ".0.0";
        if (parts.Length == 2) return parts[0] + "." + parts[1] + ".0";
        return text;
    }

    internal static bool IsHttpsUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    internal static bool IsValidSha256Hex(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 64) return false;
        foreach (char c in hex)
        {
            bool ok = (c >= '0' && c <= '9') ||
                      (c >= 'a' && c <= 'f') ||
                      (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    private static string NormalizeSha256(string hex) => hex.Trim().ToLowerInvariant();

    /// <summary>
    /// Download the file at <paramref name="downloadUrl"/>, stream-hash it, and only return a path
    /// if SHA-256 matches <paramref name="expectedSha256"/> (required, strict).
    /// Hash is of the bytes as downloaded (installer or zip package) before any extraction.
    /// </summary>
    public static async Task<DownloadUpdateResult> DownloadUpdateAsync(
        string downloadUrl,
        string expectedSha256,
        IProgress<string>? log,
        CancellationToken ct = default)
    {
        if (!IsHttpsUrl(downloadUrl))
        {
            return new DownloadUpdateResult
            {
                Error = "Update rejected: download URL must use https://."
            };
        }

        if (!IsValidSha256Hex(expectedSha256))
        {
            return new DownloadUpdateResult
            {
                Error = "Update rejected: missing or invalid sha256 in update channel (manifest required)."
            };
        }

        string expected = NormalizeSha256(expectedSha256);

        string tempDir = Path.Combine(Path.GetTempPath(), "IMVUCompanion_update");
        Directory.CreateDirectory(tempDir);

        string fileName;
        try
        {
            fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        }
        catch
        {
            return new DownloadUpdateResult { Error = "Update rejected: invalid download URL." };
        }

        if (string.IsNullOrEmpty(fileName))
            fileName = "IMVUCompanion_download.bin";

        // Unique path per download (avoid fixed names for the payload too)
        string dest = Path.Combine(tempDir, Path.GetFileNameWithoutExtension(Path.GetRandomFileName()) + "_" + fileName);

        log?.Report("Downloading update…");
        try
        {
            using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(dest);

            // Stream download + SHA-256 without buffering the whole file in memory
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] buffer = new byte[81920];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                hasher.AppendData(buffer.AsSpan(0, read));
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            await output.FlushAsync(ct);
            string actual = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                try { File.Delete(dest); } catch { /* best effort */ }
                return new DownloadUpdateResult
                {
                    Error =
                        "Integrity check failed: downloaded file SHA-256 does not match the update channel. " +
                        $"expected={expected} actual={actual}"
                };
            }

            log?.Report("Update package verified (SHA-256).");
        }
        catch (Exception ex)
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            return new DownloadUpdateResult { Error = "Download failed: " + ex.Message };
        }

        // Zip path: package itself was hashed above. Extract then only accept exact app exe name.
        if (dest.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            log?.Report("Extracting verified update package…");
            string extractDir = Path.Combine(tempDir, "extracted_" + Path.GetFileNameWithoutExtension(Path.GetRandomFileName()));
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            try
            {
                ZipFile.ExtractToDirectory(dest, extractDir);
            }
            catch (Exception ex)
            {
                return new DownloadUpdateResult { Error = "Extract failed: " + ex.Message };
            }

            string? exe = FindAppExeExact(extractDir);
            if (exe == null)
            {
                return new DownloadUpdateResult
                {
                    Error = $"Update package does not contain {ExpectedAppExeName}."
                };
            }
            return new DownloadUpdateResult { Path = exe };
        }

        if (dest.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new DownloadUpdateResult { Path = dest };

        return new DownloadUpdateResult
        {
            Error = "Update package type is not supported (expected .exe installer or .zip)."
        };
    }

    /// <summary>Only IMVUCompanion.exe by exact name — never a generic *.exe fallback.</summary>
    private static string? FindAppExeExact(string dir)
    {
        string direct = Path.Combine(dir, ExpectedAppExeName);
        if (File.Exists(direct)) return direct;

        foreach (var file in Directory.EnumerateFiles(dir, ExpectedAppExeName, SearchOption.AllDirectories))
            return file;

        return null;
    }

    public static bool TryScheduleApplyAndRestart(string newExePath, out string? error)
    {
        error = null;
        if (!File.Exists(newExePath))
        {
            error = "Downloaded update file is missing.";
            return false;
        }

        string fileName = Path.GetFileName(newExePath);
        string rand = Path.GetFileNameWithoutExtension(Path.GetRandomFileName());

        if (fileName.Contains("Setup", StringComparison.OrdinalIgnoreCase))
        {
            string setupDir = Path.GetDirectoryName(newExePath) ?? Path.GetTempPath();
            string launchScript = Path.Combine(setupDir, $"imvu_finish_update_{rand}.cmd");
            string localApp = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "IMVU Companion", ExpectedAppExeName);
            string progFiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "IMVU Companion", ExpectedAppExeName);
            File.WriteAllText(launchScript, $"""
@echo off
setlocal
"{newExePath}" /VERYSILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART
if exist "{localApp}" (
  start "" "{localApp}"
  goto done
)
if exist "{progFiles}" (
  start "" "{progFiles}"
  goto done
)
:done
del "%~f0" 2>nul
""");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = launchScript,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
            return true;
        }

        string? currentExe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExe) || !currentExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            error = "Updates apply only to the installed .exe (not dotnet run).";
            return false;
        }

        string tempDir = Path.GetDirectoryName(newExePath) ?? Path.GetTempPath();
        string staged = Path.Combine(tempDir, $"IMVUCompanion_new_{rand}.exe");
        File.Copy(newExePath, staged, true);

        int pid = Environment.ProcessId;
        string script = Path.Combine(tempDir, $"imvu_apply_update_{rand}.cmd");
        // Cap wait: 60 × 1s. PID reuse would otherwise spin forever.
        File.WriteAllText(script, $"""
@echo off
setlocal
set WAIT=0
:waitloop
if %WAIT% GEQ 60 (
  echo IMVUCompanion update: timed out waiting for process {pid} to exit.
  del "{staged}" 2>nul
  del "%~f0" 2>nul
  exit /b 1
)
tasklist /FI "PID eq {pid}" 2>nul | find "{pid}" >nul
if %ERRORLEVEL%==0 (
  timeout /t 1 /nobreak >nul
  set /a WAIT+=1
  goto waitloop
)
copy /Y "{staged}" "{currentExe}"
if %ERRORLEVEL% NEQ 0 (
  del "{staged}" 2>nul
  del "%~f0" 2>nul
  exit /b 1
)
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
