using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace IMVUCompanion;

public partial class MainWindow
{
    private const string ImvuHomeUrl = "https://www.imvu.com/next/";
    private const string ImvuChatUrl = "https://www.imvu.com/next/chat/";
    private bool _webViewReady;
    private string? _observerBoundUrl;

    private static string WebViewProfileDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMVUCompanion", "WebView2");

    private bool IsWebViewReady => _webViewReady && ImvuWebView?.CoreWebView2 != null;

    private async Task InitWebViewAsync()
    {
        try
        {
            Directory.CreateDirectory(WebViewProfileDir());
            var env = await CoreWebView2Environment.CreateAsync(null, WebViewProfileDir());
            await ImvuWebView.EnsureCoreWebView2Async(env);

            var core = ImvuWebView.CoreWebView2;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;

            core.NavigationCompleted += Core_NavigationCompleted;
            core.WebMessageReceived += Core_WebMessageReceived;
            core.FrameCreated += (_, e) =>
            {
                _ = e.Frame.ExecuteScriptAsync(ImvuScripts.ActiveChatHook);
            };

            // Capture activeChat in every frame when IMVU registers it (no UI clicks).
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ImvuScripts.ActiveChatHook);

            _webViewReady = true;
            core.Navigate(ImvuHomeUrl);
            UpdatePageStatus();
            AppendLog("IMVU loaded inside app. Log in, open your chat room, then Start Bot.", LogCategory.Info);
        }
        catch (Exception ex)
        {
            AppendLog("WebView init failed: " + ex.Message, LogCategory.Error);
            AppendLog("Install WebView2 Runtime: https://developer.microsoft.com/microsoft-edge/webview2/", LogCategory.Warning);
            UpdateStatusText("WebView failed — install WebView2 Runtime");
        }
    }

    private void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.BeginInvoke(async () =>
        {
            UpdatePageStatus();
            // Navigation (Home / Chat / Reload / leave) may drop the room — re-check presence
            if (_botRunning && IsWebViewReady)
                await CheckRoomPresenceWhileBotRunningAsync();
        });
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
                // Only process while bot is active in a live room (paused-no-room skips)
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
            state = "Bot PAUSED (no room)";
        else if (_botRunning)
            state = "Bot RUNNING";
        else
            state = "Ready";

        bool urlChat = url.Contains("/chat", StringComparison.OrdinalIgnoreCase) ||
                       url.Contains("room", StringComparison.OrdinalIgnoreCase);
        UpdateStatusText(urlChat
            ? $"{state} | Chat URL — Start Bot needs active room UI"
            : $"{state} | Open a chat room on the left");
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebViewReady) ImvuWebView.CoreWebView2.Navigate(ImvuHomeUrl);
        // Room leave via navigation is detected by CheckRoomPresenceWhileBotRunningAsync
    }

    private void NavChat_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebViewReady) ImvuWebView.CoreWebView2.Navigate(ImvuChatUrl);
    }

    private void NavReload_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebViewReady) ImvuWebView.CoreWebView2.Reload();
    }

}
