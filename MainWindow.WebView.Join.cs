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
    private async Task SeedGreetedFromExistingChatAsync()
    {
        if (!IsWebViewReady) return;
        try
        {
            string? raw = await RunJsStringAsync(ImvuScripts.CollectJoinUidsFull, logErrors: false);
            if (string.IsNullOrWhiteSpace(raw)) return;
            foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!string.IsNullOrWhiteSpace(part))
                    _greetedUserIds.Add(part);
            }
            AppendLog($"Seeded {_greetedUserIds.Count} greeted uid(s) from chat history.");
        }
        catch (Exception ex) { AppendLog("Seed greeted uids err: " + ex.Message); }
    }

    private async Task RunChatDiagnosticsAsync()
    {
        var diag = await RunJsStringAsync(ImvuScripts.FindChatRoot + """
const r = __imvuFindChatRoot();
const url = location.href;
const iframes = document.querySelectorAll('iframe').length;
let joinLines = 0;
const text = (r.cont.innerText || r.cont.textContent || '');
for (const line of text.split(/[\n\r]+/)) {
    if (/joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room/i.test(line)) joinLines++;
}
return 'url=' + url
    + ' | iframes=' + iframes
    + ' | stream=' + (r.hasStream ? 'yes' : 'no')
    + ' | input=' + (r.hasInput ? 'yes' : 'no')
    + ' | joinLines~' + joinLines
    + ' | observer=' + (window._o ? 'on' : 'off');
""", logErrors: true);
        AppendLog("Diag: " + (diag ?? "JS failed"), LogCategory.Info);
    }

    private async Task TeardownChatObserverWebView()
    {
        await RunJsVoidAsync("""
if (window._joinPoll) { clearInterval(window._joinPoll); window._joinPoll = null; }
if (window._cmdPoll) { clearInterval(window._cmdPoll); window._cmdPoll = null; }
if (window._o) { try { window._o.disconnect(); } catch(e){} window._o = null; }
""");
    }

    private async Task SetupChatObserverWebView()
    {
        if (!IsWebViewReady) return;
        string url = ImvuWebView.CoreWebView2.Source;
        _observerBoundUrl = url;

        // Capture activeChat from top + same-origin iframes (room UI is often framed).
        await RunJsVoidAsync(ImvuScripts.ActiveChatHook + """
try {
  if (typeof window.__imvuCompanionInstallHooks === 'function') {
    for (const f of document.querySelectorAll('iframe')) {
      try { window.__imvuCompanionInstallHooks(f.contentWindow); } catch (e) {}
    }
  }
  for (const f of document.querySelectorAll('iframe')) {
    try {
      const ac = f.contentWindow && f.contentWindow.__imvuCompanionActiveChat;
      if (ac) { window.__imvuCompanionActiveChat = ac; break; }
    } catch (e) {}
  }
} catch (e) {}
""", logErrors: false);

        bool ok = await RunJsVoidAsync(ImvuScripts.ChatObserverFull, logErrors: true);
        var hint = await RunJsStringAsync("return window._lastChatContainer || '';", logErrors: true);
        AppendLog(ok ? "Observer installed — " + (string.IsNullOrEmpty(hint) ? "(no container hint)" : hint)
                      : "Observer FAILED to install — check JS error above", LogCategory.Info);

        var probe = await RunJsStringAsync(ImvuScripts.FindChatRoot + ImvuScripts.ProactiveWhisper + "return silentWhisperProbe();", logErrors: false);
        if (!string.IsNullOrEmpty(probe))
            AppendLog("Whisper API: " + probe, LogCategory.Info);

        await RunChatDiagnosticsAsync();
    }
}
