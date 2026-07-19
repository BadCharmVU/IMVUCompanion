using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace IMVUCompanion;

public partial class MainWindow
{
    private const bool ShowUpdateButtonWhenUpToDate = true;

    private ButtonGlowAnimator? _updateGlow;
    private UpdateCheckResult? _lastUpdateCheck;
    private bool _updateInProgress;
    private DispatcherTimer? _updatePollTimer;
    private CancellationTokenSource? _updateCts;

    private ButtonGlowAnimator UpdateGlow => _updateGlow ??= new ButtonGlowAnimator(UpdateBtn);

    private void InitAutoUpdateUi()
    {
        Title = AppVersion.WindowTitle;

        // Dev build (bin\Release\... under the repo): never offer installer updates.
        // You test the code you just compiled; installed copies on other PCs use the update button.
        if (AppVersion.IsDevBuild)
        {
            SetUpdateButtonState(UpdateUiState.Dev);
            return;
        }

        SetUpdateButtonState(UpdateUiState.UpToDate);
        _ = CheckForUpdatesAsync(manual: false);
        StartUpdatePollTimer();
    }

    private void StartUpdatePollTimer()
    {
        _updatePollTimer?.Stop();
        _updatePollTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        _updatePollTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(manual: false);
        _updatePollTimer.Start();
    }

    private enum UpdateUiState
    {
        Checking,
        UpToDate,
        Available,
        Updating,
        Error,
        Dev
    }

    private void SetUpdateButtonState(UpdateUiState state, string? detail = null)
    {
        if (UpdateBtn == null) return;

        void apply()
        {
            bool show = ShowUpdateButtonWhenUpToDate
                || state == UpdateUiState.Available
                || state == UpdateUiState.Updating
                || state == UpdateUiState.Dev;
            UpdateBtn.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            switch (state)
            {
                case UpdateUiState.Checking:
                    UpdateBtn.Content = $"{AppVersion.ShortLabel} - Checking…";
                    UpdateBtn.IsEnabled = false;
                    UpdateGlow.Stop();
                    break;
                case UpdateUiState.UpToDate:
                    UpdateBtn.Content = $"{AppVersion.ShortLabel} - Up To Date";
                    UpdateBtn.IsEnabled = true;
                    UpdateBtn.ToolTip = "Click to check for updates";
                    UpdateGlow.Stop();
                    break;
                case UpdateUiState.Dev:
                    UpdateBtn.Content = $"{AppVersion.ShortLabel} - Dev build";
                    UpdateBtn.IsEnabled = false;
                    UpdateBtn.ToolTip =
                        "Local development build (bin\\Release\\...).\n" +
                        "Not installed — auto-update is for the installed app on other PCs.\n" +
                        "Rebuild with scripts\\Run-Dev.ps1 after every change.";
                    UpdateGlow.Stop();
                    break;
                case UpdateUiState.Available:
                    string remote = AppVersion.FormatLabel(_lastUpdateCheck?.RemoteVersion);
                    bool ready = _lastUpdateCheck?.CanDownload == true;
                    UpdateBtn.Content = ready
                        ? $"{AppVersion.ShortLabel} → {remote} Update"
                        : $"{AppVersion.ShortLabel} → {remote} (pending)";
                    UpdateBtn.IsEnabled = true;
                    UpdateBtn.ToolTip = ready
                        ? _lastUpdateCheck?.ReleaseNotes
                        : "A newer release was published, but install waits until the update channel " +
                          "lists a matching https URL and sha256. Click to re-check.";
                    UpdateGlow.SetActive(ready);
                    break;
                case UpdateUiState.Updating:
                    UpdateBtn.Content = "Updating…";
                    UpdateBtn.IsEnabled = false;
                    UpdateGlow.SetActive(true);
                    break;
                case UpdateUiState.Error:
                    UpdateBtn.Content = $"{AppVersion.ShortLabel} - Check Failed";
                    UpdateBtn.IsEnabled = true;
                    UpdateBtn.ToolTip = detail;
                    UpdateGlow.Stop();
                    break;
            }
        }

        if (Dispatcher.CheckAccess()) apply();
        else Dispatcher.BeginInvoke(apply);
    }

    private async void UpdateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_updateInProgress) return;
        if (AppVersion.IsDevBuild)
        {
            AppendLog("Dev build: updates apply only to the installed app (other PC).", LogCategory.Info);
            SetUpdateButtonState(UpdateUiState.Dev);
            return;
        }

        if (_lastUpdateCheck?.UpdateAvailable == true && _lastUpdateCheck.CanDownload)
        {
            await ApplyUpdateAsync();
            return;
        }

        if (_lastUpdateCheck?.UpdateAvailable == true && !_lastUpdateCheck.CanDownload)
        {
            AppendLog(
                "A newer release was seen, but install is blocked until the update channel publishes a matching https URL and sha256.",
                LogCategory.Warning);
            await CheckForUpdatesAsync(manual: true);
            return;
        }

        await CheckForUpdatesAsync(manual: true);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateInProgress) return;
        if (AppVersion.IsDevBuild)
        {
            SetUpdateButtonState(UpdateUiState.Dev);
            return;
        }

        try
        {
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var ct = _updateCts.Token;

            SetUpdateButtonState(UpdateUiState.Checking);
            var result = await UpdateService.CheckForUpdateAsync(ct);
            _lastUpdateCheck = result;

            if (!string.IsNullOrEmpty(result.Error) && result.RemoteVersion == null)
            {
                if (manual) AppendLog("Update check: " + result.Error, LogCategory.Warning);
                SetUpdateButtonState(UpdateUiState.Error, result.Error);
                return;
            }

            if (result.UpdateAvailable)
            {
                AppendLog(
                    $"Update available: {AppVersion.ShortLabel} → {AppVersion.FormatLabel(result.RemoteVersion)}",
                    LogCategory.Info);
                if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
                    AppendLog("Release: " + result.ReleaseNotes.Trim(), LogCategory.Info);
                if (!result.CanDownload)
                {
                    AppendLog(
                        "Update channel not ready to install yet (verified manifest with sha256 required). Waiting for update channel.",
                        LogCategory.Warning);
                }
                SetUpdateButtonState(UpdateUiState.Available);
            }
            else
            {
                if (manual) AppendLog($"{AppVersion.ShortLabel} is up to date.", LogCategory.Info);
                SetUpdateButtonState(UpdateUiState.UpToDate);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog("Update check failed: " + ex.Message, LogCategory.Warning);
            SetUpdateButtonState(UpdateUiState.Error, ex.Message);
        }
    }

    private async Task ApplyUpdateAsync()
    {
        if (_lastUpdateCheck == null ||
            !_lastUpdateCheck.CanDownload ||
            string.IsNullOrEmpty(_lastUpdateCheck.DownloadUrl) ||
            string.IsNullOrEmpty(_lastUpdateCheck.Sha256))
        {
            AppendLog(
                "Cannot install update: verified channel requires https downloadUrl and sha256 from the update manifest.",
                LogCategory.Warning);
            return;
        }

        if (_botRunning)
        {
            AppendLog("Stopping bot before update…", LogCategory.Info);
            StopBot();
            await Task.Delay(500);
        }

        _updateInProgress = true;
        SetUpdateButtonState(UpdateUiState.Updating);

        try
        {
            _updateCts?.Cancel();
            _updateCts = new CancellationTokenSource();
            var ct = _updateCts.Token;

            var progress = new Progress<string>(msg => AppendLog(msg, LogCategory.Info));
            var dl = await UpdateService.DownloadUpdateAsync(
                _lastUpdateCheck.DownloadUrl,
                _lastUpdateCheck.Sha256,
                progress,
                ct);

            if (string.IsNullOrEmpty(dl.Path))
            {
                AppendLog(dl.Error ?? "Update download failed.", LogCategory.Error);
                SetUpdateButtonState(UpdateUiState.Available);
                return;
            }

            if (!UpdateService.TryScheduleApplyAndRestart(dl.Path, out string? err))
            {
                AppendLog("Update apply: " + (err ?? "unknown error"), LogCategory.Warning);
                SetUpdateButtonState(UpdateUiState.Available);
                return;
            }

            AppendLog("Update ready — restarting app…", LogCategory.Info);
            _exiting = true;
            await Task.Delay(300);
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppendLog("Update failed: " + ex.Message, LogCategory.Error);
            SetUpdateButtonState(UpdateUiState.Available);
        }
        finally
        {
            _updateInProgress = false;
        }
    }

    private void StopUpdateTimers()
    {
        _updatePollTimer?.Stop();
        _updateCts?.Cancel();
        UpdateGlow.Stop();
    }
}