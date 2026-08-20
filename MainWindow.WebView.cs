using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace IMVUCompanion;

public partial class MainWindow
{
    private const string ImvuHomeUrl = "https://www.imvu.com/next/";
    private const string ImvuChatUrl = "https://www.imvu.com/next/chat/";
    private bool _webViewReady;
    private string? _observerBoundUrl;
    private bool _imvuShellReady;
    private bool _imvuHookInjected;
    private bool _imvuNavRetried;
    private bool _splashHidden;
    private DateTime _splashShownUtc = DateTime.UtcNow;
    private int _imvuReadyPolls;
    private int _imvuLoadGen;
    private const int SplashMinMs = 3000;
    private const int SplashMaxMs = 30000;

    private static string WebViewProfileDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMVUCompanion", "WebView2");

    private bool IsWebViewReady => _webViewReady && ImvuWebView?.CoreWebView2 != null;

    private async Task InitWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(WebViewProfileDir());
            PrepareSplashOverlay();
            CoverImvuSurface();

            var options = new CoreWebView2EnvironmentOptions(
                additionalBrowserArguments:
                    "--autoplay-policy=no-user-gesture-required " +
                    "--disable-features=InterestFeedContentSuggestions,msSmartScreenProtection");
            var env = await CoreWebView2Environment.CreateAsync(null, WebViewProfileDir(), options);
            if (ImvuWebView != null)
                ImvuWebView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 0x12, 0x12, 0x1F);
            await ImvuWebView!.EnsureCoreWebView2Async(env);
            CoverImvuSurface();

            var core = ImvuWebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreHostObjectsAllowed = false;
            SoftenWebViewFingerprint(core);

            core.NavigationStarting += Core_NavigationStarting;
            core.NavigationCompleted += Core_NavigationCompleted;
            core.WebMessageReceived += Core_WebMessageReceived;
            core.FrameCreated += Core_FrameCreated;

            _webViewReady = true;
            UpdatePageStatus();
            AppendLog("Opening IMVU…", LogCategory.Info);
            core.Navigate(ImvuHomeUrl);
        }
        catch (Exception ex)
        {
            AppendLog("WebView init failed: " + ex.Message, LogCategory.Error);
            AppendLog("Install WebView2 Runtime: https://developer.microsoft.com/microsoft-edge/webview2/", LogCategory.Warning);
            UpdateStatusText("WebView failed — install WebView2 Runtime");
            _ = HideSplashAsync();
        }
    }

    private static void SoftenWebViewFingerprint(CoreWebView2 core)
    {
        try
        {
            core.Profile.PreferredTrackingPreventionLevel = CoreWebView2TrackingPreventionLevel.None;
        }
        catch { }

        try
        {
            string ua = core.Settings.UserAgent ?? "";
            if (ua.Length == 0) return;
            ua = ua.Replace(" WebView2", "", StringComparison.OrdinalIgnoreCase)
                   .Replace(" WebView", "", StringComparison.OrdinalIgnoreCase)
                   .Replace("; wv", "", StringComparison.OrdinalIgnoreCase);
            core.Settings.UserAgent = ua;
        }
        catch { }
    }

    private void Core_NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.IsRedirected) return;
        _imvuShellReady = false;
        _imvuHookInjected = false;
        _imvuReadyPolls = 0;
        _imvuLoadGen++;
    }

    private void Core_FrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs e)
    {
        if (!_imvuShellReady) return;
        var frame = e.Frame;
        _ = Dispatcher.BeginInvoke(async () =>
        {
            try { await frame.ExecuteScriptAsync(ImvuScripts.ActiveChatHook); }
            catch { }
        });
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_isShuttingDown) return;
            UpdatePageStatus();
            if (!IsWebViewReady) return;

            if (!e.IsSuccess && !_imvuNavRetried)
            {
                _imvuNavRetried = true;
                await Task.Delay(1200);
                if (IsWebViewReady)
                    ImvuWebView.CoreWebView2.Reload();
                return;
            }

            if (e.IsSuccess)
                _imvuNavRetried = false;

            if (_isShuttingDown || _exitSplashShown) return;
            await WaitForImvuShellThenAttachAsync();

            if (_isShuttingDown || _exitSplashShown) return;
            if (IsWebViewReady)
                await CheckRoomPresenceAsync();
        });
    }

    private async Task WaitForImvuShellThenAttachAsync()
    {
        int gen = _imvuLoadGen;
        var deadline = DateTime.UtcNow.AddMilliseconds(SplashMaxMs);
        while (IsWebViewReady && DateTime.UtcNow < deadline && gen == _imvuLoadGen
               && !_isShuttingDown && !_exitSplashShown)
        {
            if (await ImvuShellLooksReadyAsync())
            {
                if (gen != _imvuLoadGen || _isShuttingDown || _exitSplashShown) return;
                _imvuShellReady = true;
                await InjectActiveChatHookIfNeededAsync();
                bool firstReveal = !_splashHidden;
                await HideSplashAsync();
                if (firstReveal && !_exitSplashShown)
                    AppendLog("IMVU is ready. Log in, open your chat room, then Start.", LogCategory.Info);
                return;
            }
            _imvuReadyPolls++;
            await Task.Delay(400);
        }

        if (gen != _imvuLoadGen || _isShuttingDown || _exitSplashShown) return;
        _imvuShellReady = true;
        await InjectActiveChatHookIfNeededAsync();
        await HideSplashAsync();
    }

    private async Task<bool> ImvuShellLooksReadyAsync()
    {
        string? state = await RunJsExpressionAsync("""
(() => {
  try {
    const h = (location.hostname || '').toLowerCase();
    if (h.indexOf('imvu.com') < 0) return 'wait';
    if (document.readyState !== 'complete') return 'wait';
    const n = document.body ? document.body.querySelectorAll('input, button, a, img, [role="button"]').length : 0;
    if (n >= 6) return 'ready';
    if (window.IMVU) return 'ready';
    const app = document.querySelector('#root, #app, main, [data-reactroot]');
    if (app && app.children && app.children.length > 0) return 'ready';
    return 'wait';
  } catch (e) { return 'wait'; }
})()
""", logErrors: false);
        return string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase);
    }

    private async Task InjectActiveChatHookIfNeededAsync()
    {
        if (_imvuHookInjected || !IsWebViewReady) return;
        _imvuHookInjected = true;
        try
        {
            await ImvuWebView.CoreWebView2.ExecuteScriptAsync(ImvuScripts.ActiveChatHook);
        }
        catch { }
    }

    private void PrepareSplashOverlay()
    {
        _exitSplashShown = false;
        _splashHidden = false;
        _splashShownUtc = DateTime.UtcNow;
        if (ImvuSplashTitle != null) ImvuSplashTitle.Text = "Loading IMVU…";
        if (ImvuSplashHint != null) ImvuSplashHint.Text = "Letting the page start on its own";
        ShowImvuSplash(reverseSpin: false);
        TryStartSplashVideo(reverse: false);
    }

    private void CoverImvuSurface()
    {
        if (ImvuWebView != null)
            ImvuWebView.Visibility = Visibility.Collapsed;
        RestoreSplashOverlay();
    }

    private void RevealImvuSurface()
    {
        if (_exitSplashShown || _isShuttingDown) return;
        if (ImvuSplashOverlay != null)
        {
            ImvuSplashOverlay.BeginAnimation(UIElement.OpacityProperty, null);
            ImvuSplashOverlay.Visibility = Visibility.Collapsed;
            ImvuSplashOverlay.Opacity = 1;
        }
        if (ImvuWebView != null)
            ImvuWebView.Visibility = Visibility.Visible;
        StopSplashVideo();
    }

    private void ShowImvuSplash(bool reverseSpin)
    {
        CoverImvuSurface();
        StartSplashSpin(reverseSpin);
    }

    private void StartSplashSpin(bool reverse)
    {
        if (ImvuSplashSpin == null) return;
        ImvuSplashSpin.BeginAnimation(RotateTransform.AngleProperty, null);
        var anim = reverse
            ? new DoubleAnimation(360, 0, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever }
            : new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever };
        ImvuSplashSpin.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    private void TryStartSplashVideo(bool reverse)
    {
        if (ImvuSplashVideo == null || ImvuSplashFallback == null) return;
        _exitVideoRewind?.Stop();
        string? path = FindSplashVideo();
        if (path == null)
        {
            ShowSplashSpinnerOnly();
            return;
        }
        try
        {
            ImvuSplashVideo.SpeedRatio = 1;
            ImvuSplashVideo.Source = new Uri(path, UriKind.Absolute);
            ImvuSplashVideo.Visibility = Visibility.Visible;
            ImvuSplashFallback.Visibility = Visibility.Collapsed;
            ImvuSplashVideo.Position = TimeSpan.Zero;
            ImvuSplashVideo.Play();
        }
        catch
        {
            ShowSplashSpinnerOnly();
        }
    }

    /// <summary>
    /// Optional intro clip (muted). Drop splash.mp4 in %LOCALAPPDATA%\IMVUCompanion,
    /// next to the exe, or Assets\. Exit plays it backwards when possible.
    /// </summary>
    private static string? FindSplashVideo()
    {
        string[] names = { "splash.mp4", "splash.webm" };
        string[] dirs =
        {
            UserDataPaths.Root,
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "Assets")
        };
        foreach (string dir in dirs)
        {
            foreach (string name in names)
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path))
                    return path;
            }
        }
        return null;
    }

    private void ImvuSplashVideo_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (!_exitSplashShown || ImvuSplashVideo == null) return;
        try
        {
            if (!ImvuSplashVideo.NaturalDuration.HasTimeSpan) return;
            ImvuSplashVideo.Pause();
            ImvuSplashVideo.Position = ImvuSplashVideo.NaturalDuration.TimeSpan;
            try
            {
                ImvuSplashVideo.SpeedRatio = -1;
                ImvuSplashVideo.Play();
                return;
            }
            catch { }
            StartExitVideoScrub();
        }
        catch
        {
            ShowSplashSpinnerOnly();
        }
    }

    private void ImvuSplashVideo_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (ImvuSplashVideo == null) return;
        if (_exitSplashShown)
        {
            _exitVideoRewind?.Stop();
            try { ImvuSplashVideo.Pause(); } catch { }
            return;
        }
        if (_splashHidden) return;
        try
        {
            ImvuSplashVideo.Position = TimeSpan.Zero;
            ImvuSplashVideo.Play();
        }
        catch { }
    }

    private void ImvuSplashVideo_MediaFailed(object sender, ExceptionRoutedEventArgs e) =>
        ShowSplashSpinnerOnly();

    private async Task HideSplashAsync()
    {
        if (_splashHidden || _exitSplashShown || _isShuttingDown) return;
        int shownMs = (int)(DateTime.UtcNow - _splashShownUtc).TotalMilliseconds;
        int wait = SplashMinMs - shownMs;
        if (wait > 0)
            await Task.Delay(wait);
        if (_splashHidden || _exitSplashShown || _isShuttingDown) return;
        _splashHidden = true;

        if (ImvuSplashOverlay == null)
        {
            StopSplashVideo();
            return;
        }

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        var done = new TaskCompletionSource();
        fade.Completed += (_, _) => done.TrySetResult();
        ImvuSplashOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
        await done.Task;
        if (_exitSplashShown || _isShuttingDown)
        {
            CoverImvuSurface();
            return;
        }
        RevealImvuSurface();
    }

    private void RestoreSplashOverlay()
    {
        if (ImvuSplashOverlay == null) return;
        ImvuSplashOverlay.BeginAnimation(UIElement.OpacityProperty, null);
        ImvuSplashOverlay.Opacity = 1;
        ImvuSplashOverlay.Visibility = Visibility.Visible;
    }

    private void StopSplashVideo()
    {
        _exitVideoRewind?.Stop();
        if (ImvuSplashVideo == null) return;
        try { ImvuSplashVideo.Stop(); } catch { }
        try { ImvuSplashVideo.SpeedRatio = 1; } catch { }
        ImvuSplashVideo.Visibility = Visibility.Collapsed;
    }

    private DispatcherTimer? _exitVideoRewind;
    private bool _exitSplashShown;

    private async Task ShowExitSplashAndHoldAsync()
    {
        if (_exitSplashShown) return;
        _exitSplashShown = true;
        _splashHidden = false;
        _splashShownUtc = DateTime.UtcNow;
        if (ImvuSplashTitle != null) ImvuSplashTitle.Text = "Closing…";
        if (ImvuSplashHint != null) ImvuSplashHint.Text = "Leaving the room quietly";
        CoverImvuSurface();
        StartSplashSpin(reverse: true);
        TryStartSplashVideo(reverse: true);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
    }

    private async Task WaitExitSplashMinAsync()
    {
        int shownMs = (int)(DateTime.UtcNow - _splashShownUtc).TotalMilliseconds;
        int wait = SplashMinMs - shownMs;
        if (wait > 0)
            await Task.Delay(wait);
    }

    private void StartExitVideoScrub()
    {
        if (ImvuSplashVideo == null) return;
        _exitVideoRewind?.Stop();
        _exitVideoRewind = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _exitVideoRewind.Tick += (_, _) =>
        {
            if (ImvuSplashVideo == null) return;
            var next = ImvuSplashVideo.Position - TimeSpan.FromMilliseconds(33);
            if (next <= TimeSpan.Zero)
            {
                ImvuSplashVideo.Position = TimeSpan.Zero;
                _exitVideoRewind?.Stop();
                return;
            }
            ImvuSplashVideo.Position = next;
        };
        _exitVideoRewind.Start();
    }

    private void ShowSplashSpinnerOnly()
    {
        _exitVideoRewind?.Stop();
        if (ImvuSplashVideo != null)
            ImvuSplashVideo.Visibility = Visibility.Collapsed;
        if (ImvuSplashFallback != null)
            ImvuSplashFallback.Visibility = Visibility.Visible;
    }

    private void NavigateImvu(string url)
    {
        if (!IsWebViewReady) return;
        try { ImvuWebView.CoreWebView2.Stop(); } catch { }
        ImvuWebView.CoreWebView2.Navigate(url);
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string? raw = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(raw)) return;
            var parts = raw.Split('\t');
            string sp = parts.Length > 0 ? parts[0] : "";
            string txt = parts.Length > 1 ? parts[1] : raw;
            bool isWhisper = parts.Length > 2 && parts[2] == "1";
            string whisperRowRef = parts.Length > 3 ? parts[3] : "";
            string joinUserId = parts.Length > 4 ? parts[4] : "";
            Dispatcher.BeginInvoke(() =>
            {
                string kind = parts.Length > 2 ? parts[2] : "0";
                bool recordedWhisper = isWhisper ||
                    (string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase) &&
                     parts.Length > 3 && parts[3] == "1");
                HandleRoomChatEvent(sp, txt, kind, joinUserId, recordedWhisper);

                bool isRoomMeta = string.Equals(kind, "leave", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "present", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase);
                if (isRoomMeta) return;

                // Greeting / triggers only while companion is active in a live room
                if (!IsBotActive) return;
                EnqueueChatLine(sp, txt, isWhisper, whisperRowRef, joinUserId);
            });
        }
        catch (Exception ex)
        {
            // Do not swallow silently — IMVU DOM changes often show up as parse failures first.
            try
            {
                AppendLog("WebMessage parse: " + ex.Message, LogCategory.Warning);
            }
            catch
            {
                // Logging itself must never crash the WebView message pump.
            }
        }
    }

    private void UpdatePageStatus()
    {
        if (!IsWebViewReady)
        {
            UpdateStatusText("Loading IMVU…");
            if (PageUrlText != null) PageUrlText.Text = "Loading…";
            return;
        }
        string url = ImvuWebView.CoreWebView2.Source;
        if (PageUrlText != null)
            PageUrlText.Text = url.Length > 48 ? url[..45] + "…" : url;

        string state;
        if (_botRunning && _botPausedNoRoom)
            state = "PAUSED (no room)";
        else if (_botRunning)
            state = "RUNNING";
        else
            state = "Ready";

        bool urlChat = url.Contains("/chat", StringComparison.OrdinalIgnoreCase) ||
                       url.Contains("room", StringComparison.OrdinalIgnoreCase);
        UpdateStatusText(urlChat
            ? $"{state} | Chat URL — Start needs active room UI"
            : $"{state} | Open a chat room on the left");
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        NavigateImvu(ImvuHomeUrl);
        // Room leave via navigation is detected by CheckRoomPresenceWhileBotRunningAsync
    }

    private void NavChat_Click(object sender, RoutedEventArgs e)
    {
        NavigateImvu(ImvuChatUrl);
    }

    private void NavReload_Click(object sender, RoutedEventArgs e)
    {
        if (!IsWebViewReady) return;
        try { ImvuWebView.CoreWebView2.Stop(); } catch { }
        ImvuWebView.CoreWebView2.Reload();
    }

}
