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
        Error
    }

    private void SetUpdateButtonState(UpdateUiState state, string? detail = null)
    {
        if (UpdateBtn == null) return;

        void apply()
        {
            bool show = ShowUpdateButtonWhenUpToDate || state == UpdateUiState.Available || state == UpdateUiState.Updating;
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
                case UpdateUiState.Available:
                    string remote = _lastUpdateCheck?.RemoteVersion != null
                        ? $"v{_lastUpdateCheck.RemoteVersion.Major}.{_lastUpdateCheck.RemoteVersion.Minor}"
                        : "New";
                    UpdateBtn.Content = $"{AppVersion.ShortLabel} → {remote} Update";
                    UpdateBtn.IsEnabled = true;
                    UpdateBtn.ToolTip = _lastUpdateCheck?.ReleaseNotes;
                    UpdateGlow.SetActive(true);
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

        if (_lastUpdateCheck?.UpdateAvailable == true && !string.IsNullOrEmpty(_lastUpdateCheck.DownloadUrl))
        {
            await ApplyUpdateAsync();
            return;
        }

        await CheckForUpdatesAsync(manual: true);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (_updateInProgress) return;

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
                AppendLog($"Update available: {AppVersion.ShortLabel} → v{result.RemoteVersion?.Major}.{result.RemoteVersion?.Minor}", LogCategory.Info);
                if (!string.IsNullOrWhiteSpace(result.ReleaseNotes))
                    AppendLog("Release: " + result.ReleaseNotes.Trim(), LogCategory.Info);
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
        if (_lastUpdateCheck == null || string.IsNullOrEmpty(_lastUpdateCheck.DownloadUrl))
        {
            AppendLog("No download URL for update.", LogCategory.Warning);
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
            string? newExe = await UpdateService.DownloadUpdateAsync(_lastUpdateCheck.DownloadUrl, progress, ct);
            if (string.IsNullOrEmpty(newExe))
            {
                AppendLog("Update download failed.", LogCategory.Error);
                SetUpdateButtonState(UpdateUiState.Available);
                return;
            }

            if (!UpdateService.TryScheduleApplyAndRestart(newExe, out string? err))
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