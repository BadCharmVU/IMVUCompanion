using System;
using System.IO;
using System.Text.Json;
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
                _ = e.Frame.ExecuteScriptAsync(ImvuActiveChatHookJs);
            };

            // Capture activeChat in every frame when IMVU registers it (no UI clicks).
            await core.AddScriptToExecuteOnDocumentCreatedAsync(ImvuActiveChatHookJs);

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
        catch { }
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

    /// <summary>ExecuteScriptAsync requires an invoked expression. Body is statements inside one IIFE.</summary>
    private static string JsIife(string body)
    {
        var t = body.Trim();
        if (t.Length == 0) return "(() => {})();";
        if (t.StartsWith("(() =>", StringComparison.Ordinal) && t.EndsWith(")();", StringComparison.Ordinal))
            return t;
        return "(() => {" + t + "})();";
    }

    private static string? ParseJsStringResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "undefined") return null;
        try { return JsonSerializer.Deserialize<string>(json); }
        catch
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                    return doc.RootElement.GetString();
                // Non-string results (numbers, objects) — stringify for diagnostics
                if (doc.RootElement.ValueKind != JsonValueKind.Null &&
                    doc.RootElement.ValueKind != JsonValueKind.Undefined)
                    return doc.RootElement.ToString();
            }
            catch { }
            // Last resort: strip surrounding quotes if present
            var t = json.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                return t[1..^1];
            return t.Length > 0 ? t : null;
        }
    }

    /// <summary>Run a complete JS expression as-is (e.g. top-level async IIFE). WebView2 awaits returned promises.</summary>
    private async Task<string?> RunJsExpressionAsync(string expression, bool logErrors = false)
    {
        if (!IsWebViewReady) return null;
        try
        {
            string json = await ImvuWebView.CoreWebView2.ExecuteScriptAsync(expression.Trim());
            return ParseJsStringResult(json);
        }
        catch (Exception ex)
        {
            if (logErrors) AppendLog("JS error: " + ex.Message, LogCategory.Warning);
            return null;
        }
    }

    private async Task<string?> RunJsStringAsync(string js, bool logErrors = false)
    {
        if (!IsWebViewReady) return null;
        try
        {
            string script = JsIife(js);
            string json = await ImvuWebView.CoreWebView2.ExecuteScriptAsync(script);
            var parsed = ParseJsStringResult(json);
            if (parsed == null && logErrors && !string.IsNullOrEmpty(json) && json != "null")
                AppendLog("JS raw result: " + (json.Length > 180 ? json[..180] + "…" : json), LogCategory.Warning);
            return parsed;
        }
        catch (Exception ex)
        {
            if (logErrors) AppendLog("JS error: " + ex.Message, LogCategory.Warning);
            return null;
        }
    }

    private async Task<string[]?> RunJsStringArrayAsync(string js)
    {
        if (!IsWebViewReady) return null;
        try
        {
            string json = await ImvuWebView.CoreWebView2.ExecuteScriptAsync(JsIife(js));
            if (json == "null" || json == "undefined") return null;
            return JsonSerializer.Deserialize<string[]>(json);
        }
        catch { return null; }
    }

    private async Task<bool> RunJsVoidAsync(string js, bool logErrors = false)
    {
        if (!IsWebViewReady) return false;
        try
        {
            await ImvuWebView.CoreWebView2.ExecuteScriptAsync(JsIife(js));
            return true;
        }
        catch (Exception ex)
        {
            if (logErrors) AppendLog("JS error: " + ex.Message, LogCategory.Warning);
            return false;
        }
    }

    /// <summary>
    /// Trusted mouse click via CDP. Synthetic DOM events are often ignored by IMVU's user menu;
    /// reply-whisper works with JS clicks because that control listens differently.
    /// </summary>
    private async Task<bool> CdpMouseClickAsync(double x, double y, string button = "left")
    {
        if (!IsWebViewReady) return false;
        try
        {
            var core = ImvuWebView.CoreWebView2;
            string Move(string type, string btn) =>
                $$"""{"type":"{{type}}","x":{{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":{{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"button":"{{btn}}","buttons":{{(type == "mousePressed" ? "1" : "0")}},"clickCount":1}""";

            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                $$"""{"type":"mouseMoved","x":{{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":{{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Move("mousePressed", button));
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Move("mouseReleased", button));
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("CDP click failed: " + ex.Message, LogCategory.Warning);
            return false;
        }
    }

    private static bool TryParsePoint(string? s, out double x, out double y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(',');
        if (parts.Length < 2) return false;
        return double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out y);
    }

    private const string FindChatRootJs = """
function __imvuFindChatRoot() {
    function findInDoc(doc) {
        const cont = doc.querySelector('div.chat-stream2, [class*="chat-stream2"]');
        const inp = doc.querySelector('div.input-container, [class*="input-container"]');
        if (cont || inp) return { doc, cont: cont || doc.body, hasStream: !!cont, hasInput: !!inp };
        return null;
    }
    let r = findInDoc(document);
    if (r) return r;
    for (const frame of document.querySelectorAll('iframe')) {
        try {
            const fd = frame.contentDocument || frame.contentWindow?.document;
            if (!fd) continue;
            r = findInDoc(fd);
            if (r) return r;
        } catch (e) {}
    }
    return { doc: document, cont: document.body, hasStream: false, hasInput: false };
}
""";

    /// <summary>
    /// True only when chat-stream + chat input are present (real room UI), not just /chat URL.
    /// </summary>
    private async Task<bool> IsActiveRoomPresentAsync()
    {
        if (!IsWebViewReady) return false;
        string url = ImvuWebView.CoreWebView2.Source ?? "";
        if (!url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var detected = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
// Active room = message stream + compose input (not lobby-only)
return (r.hasStream && r.hasInput) ? 'yes' : 'no';
""", logErrors: false);
        return detected == "yes";
    }

    private async Task<bool> EnsureChatPageAsync()
    {
        if (!IsWebViewReady)
        {
            AppendLog("IMVU browser is still loading. Wait a moment and try again.", LogCategory.Warning);
            return false;
        }
        string url = ImvuWebView.CoreWebView2.Source;
        if (!url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase))
        {
            AppendLog("Navigate to IMVU on the left panel first.", LogCategory.Warning);
            return false;
        }
        if (!await IsActiveRoomPresentAsync())
        {
            AppendLog("No active chat room (need stream + input). Open a room, then try again.", LogCategory.Warning);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Best-effort leave so the account does not stay seated after app exit.
    /// Tries API/UI leave, then navigates Home and waits briefly.
    /// </summary>
    private async Task LeaveImvuRoomAsync()
    {
        if (!IsWebViewReady) return;
        try
        {
            // 1) Prefer in-page leave if activeChat exposes it
            await RunJsStringAsync(FindChatRootJs + ProactiveWhisperJs + """
function tryLeave() {
  const chat = (typeof __findActiveChat === 'function' && __findActiveChat())
    || window.__imvuCompanionActiveChat
    || (window.top && window.top.__imvuCompanionActiveChat);
  if (chat) {
    for (const name of ['leaveRoom','leave','exitRoom','disconnect','close','exit']) {
      try {
        if (typeof chat[name] === 'function') {
          chat[name]();
          return 'api:' + name;
        }
      } catch (e) {}
    }
    try {
      if (typeof chat.resetMessageTarget === 'function') chat.resetMessageTarget();
    } catch (e) {}
  }
  // 2) Click leave-looking controls
  const docs = typeof __imvuAllDocs === 'function' ? __imvuAllDocs() : [document];
  for (const doc of docs) {
    for (const el of doc.querySelectorAll('button, a, [role="button"], li, span, div')) {
      try {
        const t = (el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
        if (!t || t.length > 32) continue;
        if (t === 'leave' || t === 'leave room' || t === 'exit room' || t === 'exit') {
          const r = el.getBoundingClientRect();
          if (r.width > 0 && r.height > 0) {
            el.click();
            return 'click:' + t;
          }
        }
      } catch (e) {}
    }
  }
  return 'none';
}
return tryLeave();
""", logErrors: false);

            // 3) Navigate away from room so IMQ session tears down
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                try { ImvuWebView.CoreWebView2.NavigationCompleted -= OnNav; } catch { }
                tcs.TrySetResult(e.IsSuccess);
            }
            try
            {
                ImvuWebView.CoreWebView2.NavigationCompleted += OnNav;
                ImvuWebView.CoreWebView2.Navigate(ImvuHomeUrl);
                await Task.WhenAny(tcs.Task, Task.Delay(4000));
            }
            finally
            {
                try { ImvuWebView.CoreWebView2.NavigationCompleted -= OnNav; } catch { }
            }
            // Brief settle so leave is committed before process ends
            await Task.Delay(800);
        }
        catch (Exception ex)
        {
            AppendLog("Leave room: " + ex.Message, LogCategory.Warning);
        }
    }

    private const string ChatInputSelector =
        "div.input-container input, div.input-container textarea, div.input-container [contenteditable], div[class*=\"input-container\"] input, div[class*=\"input-container\"] [contenteditable]";

    private const string ExitWhisperModeJs = """
const doc = __imvuFindChatRoot().doc;
const sel = '{{CHAT_INPUT_SEL}}';
function clickEl(el) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest' }); } catch(e) {}
    const evt = new MouseEvent('click', { bubbles: true, cancelable: true, view: window });
    el.dispatchEvent(evt);
    if (typeof el.click === 'function') el.click();
    return true;
}
const whisperClose = doc.querySelectorAll('.whisper-close, span.whisper-close, [class*="whisper-close"]');
for (const el of whisperClose) { if (clickEl(el)) return 'closed'; }
const panels = doc.querySelectorAll('[class*="whisper-compose"], [class*="whisper-target"], [class*="whisper-mode"], [class*="whisper-bar"], [class*="whisper-panel"]');
for (const bar of panels) {
    const btn = bar.querySelector('.whisper-close, [class*="whisper-close"], [class*="close"], [class*="icon-close"], button');
    if (clickEl(btn)) return 'closed';
}
const closers = doc.querySelectorAll('[class*="close-whisper"], [class*="cancel-whisper"], [class*="whisper-cancel"]');
for (const c of closers) { if (clickEl(c)) return 'closed'; }
const inpRef = doc.querySelector(sel);
if (inpRef) {
    inpRef.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
    inpRef.blur();
}
const still = doc.querySelector('.whisper-close, span.whisper-close, [class*="whisper-compose"], [class*="whisper-target"]');
return still ? 'still-open' : 'closed';
""";

    private async Task<string?> ExitWhisperModeAsync()
    {
        string js = ExitWhisperModeJs.Replace("{{CHAT_INPUT_SEL}}", ChatInputSelector);
        return await RunJsStringAsync(FindChatRootJs + js, logErrors: true);
    }

    private async Task EnsurePublicChatModeAsync()
    {
        await RunJsStringAsync(FindChatRootJs + ProactiveWhisperJs + "return dismissOpenUi();", logErrors: false);
        for (int i = 0; i < 4; i++)
        {
            string? r = await ExitWhisperModeAsync();
            if (r == "closed") return;
            await RunJsStringAsync(FindChatRootJs + ProactiveWhisperJs + "return dismissOpenUi();", logErrors: false);
            await Task.Delay(150);
        }
    }

    private async Task ForceDismissWhisperUiAsync() => await EnsurePublicChatModeAsync();

    /// <summary>Quick Escape/dismiss for proactive whisper retries (avoids multi-second close loops).</summary>
    private async Task QuickDismissWhisperUiAsync()
    {
        await RunJsStringAsync(FindChatRootJs + ProactiveWhisperJs + "return dismissOpenUi();", logErrors: false);
        await ExitWhisperModeAsync();
    }

    private async Task SetJoinPollPausedAsync(bool paused)
    {
        string flag = paused ? "true" : "false";
        await RunJsVoidAsync($"window._joinPollPaused = {flag};", logErrors: false);
    }

    private async Task RunPublicChatSendJsAsync(string text)
    {
        string escaped = JsonSerializer.Serialize(text);
        await RunJsVoidAsync(FindChatRootJs + $$"""
const t = {{escaped}};
const doc = __imvuFindChatRoot().doc;
const sel = '{{ChatInputSelector}}';
let inp = doc.querySelector(sel);
if (!inp) {
    const box = doc.querySelector('div.input-container, [class*="input-container"]');
    if (box) box.click();
    inp = doc.querySelector(sel);
}
if (!inp) return;
inp.focus();
if (inp.isContentEditable) {
    inp.textContent = t;
    inp.dispatchEvent(new InputEvent('input', { bubbles: true, data: t }));
} else {
    inp.value = t;
    inp.dispatchEvent(new Event('input', { bubbles: true }));
    inp.dispatchEvent(new Event('change', { bubbles: true }));
}
const opts = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true };
inp.dispatchEvent(new KeyboardEvent('keydown', opts));
inp.dispatchEvent(new KeyboardEvent('keypress', opts));
inp.dispatchEvent(new KeyboardEvent('keyup', opts));
const form = inp.closest('form');
if (form && form.requestSubmit) form.requestSubmit();
""");
    }

    private const string WhisperFindRowJs = """
function normSp(s) { return (s || '').replace(/\s+to\s+me$/i, '').trim().toLowerCase(); }
function getSp(item) {
    if (!item) return '';
    const sels = ['.cs2-name', '[class*="cs2-name"]', '[class*="username"]', '[class*="display-name"]'];
    for (const s of sels) {
        const el = item.querySelector(s);
        if (!el) continue;
        let sp = (el.textContent || el.innerText || '').trim().split(/[\n\r]/)[0].trim();
        if (sp.length >= 1 && sp.length <= 60) return sp;
    }
    return '';
}
function isWhisperRow(row) {
    let el = row;
    for (let i = 0; i < 8 && el; i++) {
        const cls = (el.className || '').toString();
        if (/\bis-presenter\b/i.test(cls)) return false;
        if (/\bwhisper\b/i.test(cls) && !/reply_from_whisper|reply-to-whisper/i.test(cls)) return true;
        el = el.parentElement;
    }
    return false;
}
function getCmdFromRow(row) {
    const raw = (row.innerText || row.textContent || '');
    for (const line of raw.split(/[\n\r]+/).map(l => l.trim()).filter(Boolean)) {
        if (/^!\S+/.test(line)) return line;
    }
    return '';
}
function findWhisperRow(cont, rowRef, targetSpeaker, targetCmd) {
    if (rowRef) {
        const byRef = cont.querySelector('[data-imvu-bot-cmd="' + rowRef + '"]');
        if (byRef) return byRef;
    }
    const want = normSp(targetSpeaker);
    const wantCmd = (targetCmd || '').trim().toLowerCase();
    const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], li');
    for (let i = rows.length - 1; i >= 0; i--) {
        const row = rows[i];
        if (!isWhisperRow(row)) continue;
        if (want && normSp(getSp(row)) !== want) continue;
        if (wantCmd) {
            const cmd = getCmdFromRow(row).trim().toLowerCase();
            if (cmd && cmd !== wantCmd) continue;
        }
        return row;
    }
    return null;
}
""";

    private const string ProactiveWhisperJs = """
function __imvuAllDocs() {
    const out = [document];
    for (const frame of document.querySelectorAll('iframe')) {
        try {
            const fd = frame.contentDocument || frame.contentWindow?.document;
            if (fd) out.push(fd);
        } catch (e) {}
    }
    return out;
}
function mapFancyLetter(cp) {
    if (cp >= 0x1D400 && cp <= 0x1D419) return String.fromCharCode(65 + cp - 0x1D400);
    if (cp >= 0x1D41A && cp <= 0x1D433) return String.fromCharCode(97 + cp - 0x1D41A);
    if (cp >= 0x1D434 && cp <= 0x1D44D) return String.fromCharCode(65 + cp - 0x1D434);
    if (cp >= 0x1D44E && cp <= 0x1D467) return String.fromCharCode(97 + cp - 0x1D44E);
    if (cp >= 0x1D468 && cp <= 0x1D481) return String.fromCharCode(65 + cp - 0x1D468);
    if (cp >= 0x1D482 && cp <= 0x1D49B) return String.fromCharCode(97 + cp - 0x1D482);
    if (cp >= 0x1D538 && cp <= 0x1D551) return String.fromCharCode(65 + cp - 0x1D538);
    if (cp >= 0x1D552 && cp <= 0x1D56B) return String.fromCharCode(97 + cp - 0x1D552);
    if (cp >= 0x1D5A0 && cp <= 0x1D5B9) return String.fromCharCode(65 + cp - 0x1D5A0);
    if (cp >= 0x1D5BA && cp <= 0x1D5D3) return String.fromCharCode(97 + cp - 0x1D5BA);
    if (cp >= 0x1D5D4 && cp <= 0x1D5ED) return String.fromCharCode(65 + cp - 0x1D5D4);
    if (cp >= 0x1D5EE && cp <= 0x1D607) return String.fromCharCode(97 + cp - 0x1D5EE);
    if (cp >= 0x1D608 && cp <= 0x1D621) return String.fromCharCode(65 + cp - 0x1D608);
    if (cp >= 0x1D622 && cp <= 0x1D63B) return String.fromCharCode(97 + cp - 0x1D622);
    if (cp >= 0x1D670 && cp <= 0x1D689) return String.fromCharCode(65 + cp - 0x1D670);
    if (cp >= 0x1D68A && cp <= 0x1D6A3) return String.fromCharCode(97 + cp - 0x1D68A);
    if (cp >= 0xFF21 && cp <= 0xFF3A) return String.fromCharCode(65 + cp - 0xFF21);
    if (cp >= 0xFF41 && cp <= 0xFF5A) return String.fromCharCode(97 + cp - 0xFF41);
    return null;
}
function foldImvuName(s) {
    if (!s) return '';
    let src = String(s);
    try { src = src.normalize('NFKC'); } catch (e) {}
    let out = '';
    for (const ch of src) {
        const cp = ch.codePointAt(0);
        const mapped = mapFancyLetter(cp);
        out += mapped !== null ? mapped : ch;
    }
    return out.replace(/^@+/, '').replace(/\s+to\s+me$/i, '').replace(/[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]/g, '')
        .replace(/\s+/g, ' ').trim().toLowerCase();
}
function normName(s) { return foldImvuName(s); }
function whisperPanelAtMention(doc) {
    const panel = whisperPanelRoot(doc);
    if (!panel) return '';
    const full = (panel.innerText || panel.textContent || '');
    const m = full.match(/@\s*([^\n\r]+)/u);
    return m ? m[1].trim() : '';
}
function cleanWhisperTargetName(raw) {
    let s = (raw || '').replace(/\s+/g, ' ').trim();
    s = s.replace(/^to\s+/i, '').replace(/^@+\s*/, '').trim();
    if (!s || s === '@') return '';
    if (foldImvuName(s).length >= 1) return s;
    const stripped = s.replace(/[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]/g, '');
    return stripped.length >= 1 ? s : '';
}
function isVisibleEl(el) {
    if (!el) return false;
    const r = el.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return false;
    const st = el.ownerDocument?.defaultView?.getComputedStyle?.(el);
    if (st && (st.display === 'none' || st.visibility === 'hidden' || st.opacity === '0')) return false;
    return true;
}
function whisperPanelRoot(doc) {
    const closes = doc.querySelectorAll('.whisper-close, span.whisper-close, [class*="whisper-close"]');
    for (const close of closes) {
        if (!isVisibleEl(close)) continue;
        return close.closest('[class*="whisper-compose"], [class*="whisper-panel"], [class*="whisper-bar"], [class*="whisper-mode"], [class*="input-container"]')
            || close.parentElement;
    }
    return null;
}
function anyWhisperComposeActive() {
    for (const doc of __imvuAllDocs()) {
        const panel = whisperPanelRoot(doc);
        if (panel && isVisibleEl(panel)) return true;
    }
    return false;
}
function getNameFromEl(el) {
    if (!el) return '';
    const sels = ['.cs2-name', '[class*="cs2-name"]', '[class*="username"]', '[class*="display-name"]', '[class*="user-name"]'];
    for (const s of sels) {
        const n = el.querySelector(s);
        if (!n) continue;
        const sp = (n.textContent || n.innerText || '').trim().split(/[\n\r]/)[0].trim();
        if (sp.length >= 1 && sp.length <= 60) return sp;
    }
    const txt = (el.innerText || el.textContent || '').trim().split(/[\n\r]+/)[0].trim();
    return txt.length <= 60 ? txt : '';
}
function fireMouse(el, type, button) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest' }); } catch (e) {}
    const win = el.ownerDocument?.defaultView || window;
    const rect = el.getBoundingClientRect();
    const cx = rect.left + Math.max(2, rect.width / 2);
    const cy = rect.top + Math.max(2, rect.height / 2);
    const opts = { bubbles: true, cancelable: true, view: win, button: button || 0, clientX: cx, clientY: cy };
    el.dispatchEvent(new MouseEvent(type, opts));
    if (type === 'click' && typeof el.click === 'function') el.click();
    return true;
}
function robustClick(el) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest' }); } catch (e) {}
    const win = el.ownerDocument?.defaultView || window;
    const rect = el.getBoundingClientRect();
    const cx = rect.left + Math.max(2, rect.width / 2);
    const cy = rect.top + Math.max(2, rect.height / 2);
    const base = { bubbles: true, cancelable: true, view: win, clientX: cx, clientY: cy };
    el.dispatchEvent(new PointerEvent('pointerdown', { ...base, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1 }));
    el.dispatchEvent(new MouseEvent('mousedown', { ...base, button: 0, buttons: 1 }));
    el.dispatchEvent(new PointerEvent('pointerup', { ...base, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 0 }));
    el.dispatchEvent(new MouseEvent('mouseup', { ...base, button: 0, buttons: 0 }));
    el.dispatchEvent(new MouseEvent('click', { ...base, button: 0 }));
    if (typeof el.click === 'function') el.click();
    return true;
}
function isJoinText(t) {
    t = (t || '').replace(/\s+/g, ' ').trim();
    if (!t || /left\s+the\s+chat/i.test(t)) return false;
    return /joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered|is\s+now\s+in\s+the\s+chat/i.test(t);
}
function parseJoinName(txt) {
    const m = txt.match(/^(.+?)\s+(joined\s+the\s+chat|has\s+joined(?:\s+the\s+room)?|joined(?:\s+the\s+room)?|entered\s+the\s+room|has\s+entered(?:\s+the\s+room)?|is\s+now\s+in\s+the\s+chat)\s*\.?\s*$/i);
    return m ? m[1].trim() : '';
}
function avatarFromJoinStructure(row) {
    if (!row) return null;
    function pickAvatar(firstDiv) {
        if (!firstDiv) return null;
        return firstDiv.querySelector('img') ||
            firstDiv.querySelector('[class*="avatar"]') ||
            firstDiv.querySelector('a, button, [role="button"]') ||
            firstDiv;
    }
    function tryTwoDivParent(node) {
        if (!node || !node.parentElement) return null;
        const kids = Array.from(node.parentElement.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length < 2) return null;
        const secondTxt = (kids[1].innerText || kids[1].textContent || '').replace(/\s+/g, ' ').trim();
        if (!isJoinText(secondTxt)) return null;
        return pickAvatar(kids[0]);
    }
    let kids = Array.from(row.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
    if (kids.length >= 2) {
        const secondTxt = (kids[1].innerText || kids[1].textContent || '').replace(/\s+/g, ' ').trim();
        if (isJoinText(secondTxt)) return pickAvatar(kids[0]);
    }
    let fromParent = tryTwoDivParent(row);
    if (fromParent) return fromParent;
    let node = row;
    for (let d = 0; node && d < 10; d++) {
        kids = Array.from(node.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length >= 2) {
            const secondTxt = (kids[1].innerText || kids[1].textContent || '').replace(/\s+/g, ' ').trim();
            if (isJoinText(secondTxt)) return pickAvatar(kids[0]);
        }
        fromParent = tryTwoDivParent(node);
        if (fromParent) return fromParent;
        node = node.parentElement;
    }
    return null;
}
function extractUserIdFromNode(node) {
    if (!node || !node.getAttribute) return '';
    const dataId = node.getAttribute('data-id') || '';
    const m = dataId.match(/user\/user-(\d+)/i);
    return m ? m[1] : '';
}
function extractUserIdFromWrapper(wrapper) {
    if (!wrapper) return '';
    let node = wrapper;
    for (let d = 0; node && d < 12; d++) {
        const uid = extractUserIdFromNode(node);
        if (uid) return uid;
        node = node.parentElement;
    }
    return '';
}
function getJoinRowWrapper(row) {
    if (!row) return null;
    let node = row;
    let fallback = null;
    for (let d = 0; node && d < 12; d++) {
        const kids = Array.from(node.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length >= 2) {
            const secondTxt = (kids[1].innerText || kids[1].textContent || '').replace(/\s+/g, ' ').trim();
            if (isJoinText(secondTxt)) {
                if (extractUserIdFromNode(node)) return node;
                if (!fallback) fallback = node;
            }
        }
        node = node.parentElement;
    }
    return fallback || row;
}
function joinNameFromAvatar(wrapper) {
    if (!wrapper) return '';
    const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
    if (kids.length < 1) return '';
    const firstDiv = kids[0];
    const img = firstDiv.querySelector('img');
    if (img) {
        const alt = (img.alt || img.getAttribute('title') || img.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim();
        if (alt.length >= 1 && alt.length <= 60 && !isJoinText(alt) && !/^https?:/i.test(alt)) return alt;
    }
    const link = firstDiv.querySelector('a[title], [title], [aria-label], [data-username], [data-user]');
    if (link) {
        const t = (link.getAttribute('title') || link.getAttribute('aria-label') || link.getAttribute('data-username') || link.getAttribute('data-user') || '').replace(/\s+/g, ' ').trim();
        if (t.length >= 1 && t.length <= 60 && !isJoinText(t)) return t;
    }
    const firstTxt = (firstDiv.innerText || firstDiv.textContent || '').replace(/\s+/g, ' ').trim();
    if (firstTxt.length >= 1 && firstTxt.length <= 60 && !isJoinText(firstTxt)) return firstTxt;
    return getNameFromEl(firstDiv);
}
function joinNameFromWrapper(wrapper) {
    if (!wrapper) return '';
    const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
    if (kids.length >= 2) {
        const txt = (kids[1].innerText || kids[1].textContent || '').replace(/\s+/g, ' ').trim();
        let name = parseJoinName(txt);
        if (name) return name;
        if (isJoinText(txt)) {
            name = joinNameFromAvatar(wrapper);
            if (name) return name;
        }
    }
    const full = (wrapper.innerText || '').replace(/\s+/g, ' ').trim();
    let name = parseJoinName(full);
    if (name) return name;
    name = joinNameFromAvatar(wrapper);
    if (name) return name;
    return getNameFromEl(wrapper);
}
function findJoinRowByRef(joinRef) {
    if (!joinRef) return null;
    for (const doc of __imvuAllDocs()) {
        const row = doc.querySelector('[data-imvu-bot-join="' + joinRef + '"]');
        if (row) return row;
    }
    const cont = __imvuFindChatRoot().cont;
    return cont.querySelector('[data-imvu-bot-join="' + joinRef + '"]');
}
function findJoinRowByUserId(userId) {
    if (!userId) return null;
    const sel = '[data-imvu-bot-user-id="' + userId + '"]';
    for (const doc of __imvuAllDocs()) {
        const row = doc.querySelector(sel);
        if (row) return row;
    }
    const cont = __imvuFindChatRoot().cont;
    let row = cont.querySelector(sel);
    if (row) return row;
    for (const el of cont.querySelectorAll('[data-id*="user/user-' + userId + '"], [data-id*="user-' + userId + '"]')) {
        const wrapper = getJoinRowWrapper(el) || el;
        const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length >= 2) {
            const secondTxt = (kids[1].innerText || kids[1].textContent || '').replace(/\s+/g, ' ').trim();
            if (isJoinText(secondTxt)) return wrapper;
        }
    }
    return null;
}
function resolveJoinWrapper(joinRef, userId) {
    let row = findJoinRowByRef(joinRef);
    if (!row && userId) row = findJoinRowByUserId(userId);
    if (!row) return null;
    return getJoinRowWrapper(row) || row;
}
function hasVisibleName(name) {
    const s = (name || '').replace(/[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]/g, '').trim();
    return /[\p{L}\p{N}]/u.test(s);
}
function parseWhisperTargetFromText(txt) {
    txt = (txt || '').replace(/\s+/g, ' ').trim();
    if (!txt || txt.length > 160) return '';
    let m = txt.match(/whisper(?:ing)?\s+to\s+@?\s*([^\n\r]+?)(?:\s*[\n\r]|$)/i);
    if (m) return cleanWhisperTargetName(m[1]);
    m = txt.match(/^to\s+@?\s*([^\n\r]+?)(?:\s*[\n\r]|$)/i);
    if (m) return cleanWhisperTargetName(m[1]);
    m = txt.match(/message\s+to\s+@?\s*([^\n\r]+?)(?:\s*[\n\r]|$)/i);
    if (m) return cleanWhisperTargetName(m[1]);
    m = txt.match(/@([^\s\n\r]{2,50})/);
    if (m) return cleanWhisperTargetName(m[1]);
    return cleanWhisperTargetName(txt.split(/[\n\r]/)[0].trim());
}
function getWhisperComposeTarget() {
    for (const doc of __imvuAllDocs()) {
        const atMention = whisperPanelAtMention(doc);
        if (atMention) return atMention;
        const panel = whisperPanelRoot(doc);
        if (panel) {
            for (const sel of ['.cs2-name', '[class*="cs2-name"]', '[class*="username"]', '[class*="display-name"]', '[class*="whisper-target"]', '[class*="whisper-to"]', '[class*="recipient"]']) {
                for (const nameEl of panel.querySelectorAll(sel)) {
                    const n = cleanWhisperTargetName(nameEl.textContent || nameEl.innerText || '');
                    if (n) return n;
                }
            }
            const parsed = parseWhisperTargetFromText(panel.innerText || panel.textContent || '');
            if (parsed) return parsed;
        }
        for (const inp of doc.querySelectorAll('input, textarea, [contenteditable]')) {
            const ph = (inp.getAttribute('placeholder') || inp.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim();
            const pm = ph.match(/whisper(?:ing)?\s+to\s+@?\s*(.+)/i) || ph.match(/^to\s+@?\s*(.+)/i);
            if (pm) {
                const n = cleanWhisperTargetName(pm[1]);
                if (n) return n;
            }
        }
    }
    return '';
}
function whisperTargetLooksLikeBot(botName) {
    if (!anyWhisperComposeActive()) return false;
    const bot = foldImvuName(botName || '');
    if (!bot) return false;
    const target = getWhisperComposeTarget();
    if (target && foldImvuName(target) === bot) return true;
    for (const doc of __imvuAllDocs()) {
        const panel = whisperPanelRoot(doc);
        if (!panel || !isVisibleEl(panel)) continue;
        const mention = whisperPanelAtMention(doc);
        if (mention && foldImvuName(mention) === bot) return true;
        const full = (panel.innerText || panel.textContent || '');
        if (full.includes('@') && foldImvuName(full) === bot) return true;
        for (const el of panel.querySelectorAll('[class*="whisper-target"], [class*="whisper-to"], [class*="whisper-recipient"], [class*="whisper-header"]')) {
            if (!isVisibleEl(el)) continue;
            const txt = foldImvuName((el.innerText || el.textContent || '').replace(/\s+/g, ' '));
            if (txt && txt === bot) return true;
            const aria = foldImvuName(el.getAttribute('aria-label') || el.getAttribute('title') || '');
            if (aria && (aria === bot || aria === 'to ' + bot)) return true;
        }
    }
    return false;
}
function namesRoughlyMatch(got, want) {
    if (!got || !want) return false;
    if (got === want) return true;
    if (got.includes(want) || want.includes(got)) return true;
    return false;
}
function verifyWhisperTarget(expectedName, botName, trustJoinMenu, trustUserId) {
    const want = foldImvuName(expectedName || '');
    const bot = foldImvuName(botName || '');
    if (bot && want && want === bot) return 'target-is-bot';
    const composeActive = anyWhisperComposeActive();
    if (composeActive && whisperTargetLooksLikeBot(botName)) return 'target-is-bot';
    const target = getWhisperComposeTarget();
    const got = foldImvuName(cleanWhisperTargetName(target) || target);
    if (!got) {
        if (composeActive) {
            if (trustJoinMenu && trustUserId) return 'ok-trusted';
            return 'compose-open';
        }
        return 'no-target';
    }
    if (bot && got === bot) return 'target-is-bot';
    if (want) {
        if (namesRoughlyMatch(got, want)) return 'ok';
        if (trustJoinMenu && trustUserId && bot && got !== bot) return 'ok-trusted';
        return 'target-mismatch:' + target + ' [folded:' + got + ' vs ' + want + ']';
    }
    if (trustJoinMenu && bot && got !== bot) return 'ok-trusted';
    return 'compose-open';
}
function whisperTargetDebug() {
    const raw = getWhisperComposeTarget();
    if (!raw) return '(unreadable)';
    const folded = foldImvuName(raw);
    return folded && folded !== raw.toLowerCase() ? raw + ' [fold:' + folded + ']' : raw;
}
function isInsideWhisperCompose(el) {
    return !!(el && el.closest && el.closest('[class*="whisper-compose"], [class*="whisper-panel"], [class*="whisper-bar"], [class*="whisper-mode"]'));
}
function findParticipantAvatarForName(targetName) {
    const want = foldImvuName(targetName);
    if (!want) return null;
    const sels = [
        '[class*="participant"]', '[class*="audience"]', '[class*="room-user"]',
        '[class*="chat-user"]', '[class*="user-list"] li', '[class*="members"] li',
        '[class*="presence"] li', '[class*="avatar-list"] li', '[class*="room-users"]',
        '[class*="chat-participants"]', '[class*="users-list"]', '[class*="viewer"]'
    ];
    for (const doc of __imvuAllDocs()) {
        for (const nameEl of doc.querySelectorAll('.cs2-name, [class*="cs2-name"]')) {
            if (isInsideWhisperCompose(nameEl)) continue;
            if (!namesRoughlyMatch(foldImvuName(nameEl.textContent || nameEl.innerText), want)) continue;
            const row = nameEl.closest('li, [class*="participant"], [class*="user-row"], [class*="room-user"], [class*="chat-user"]') || nameEl.parentElement;
            if (!row) continue;
            return row.querySelector('img, [class*="avatar"]') || pickClickable(nameEl);
        }
        for (const rs of sels) {
            for (const item of doc.querySelectorAll(rs)) {
                if (isInsideWhisperCompose(item)) continue;
                const name = foldImvuName(getNameFromEl(item));
                if (!namesRoughlyMatch(name, want)) continue;
                return item.querySelector('img') || item.querySelector('[class*="avatar"]') || pickClickable(item);
            }
        }
    }
    return null;
}
function joinAvatarForExpected(wrapper, expectedName, botName) {
    const want = foldImvuName(expectedName);
    const bot = foldImvuName(botName || '');
    const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
    if (kids.length < 1) return null;
    const firstDiv = kids[0];
    for (const img of firstDiv.querySelectorAll('img')) {
        const fold = foldImvuName(img.alt || img.getAttribute('title') || img.getAttribute('aria-label') || '');
        if (bot && fold === bot) continue;
        if (want && fold && namesRoughlyMatch(fold, want)) return img.closest('a') || img;
    }
    const av = joinAvatarClickTarget(wrapper);
    if (!av) return null;
    const fold = foldImvuName(joinNameFromAvatar(wrapper));
    if (bot && fold === bot) return null;
    return av;
}
function rightClickElement(el) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest' }); } catch (e) {}
    fireMouse(el, 'contextmenu', 2);
    fireMouse(el, 'mousedown', 2);
    fireMouse(el, 'mouseup', 2);
    return true;
}
function tryProactiveWhisperClick(strategy, joinRef, expectedName, botName) {
    const want = foldImvuName(expectedName);
    const bot = foldImvuName(botName || '');
    if (bot && want && want === bot) return 'joiner-is-bot';
    let el = null;
    const row = findJoinRowByRef(joinRef);
    const wrapper = row ? (getJoinRowWrapper(row) || row) : null;
    if (strategy === 'join-cs2-name' && wrapper) {
        el = wrapper.querySelector('.cs2-name, [class*="cs2-name"], [class*="username"], [class*="display-name"]');
    } else if (strategy === 'join-text-div' && wrapper) {
        const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length >= 2) el = kids[1];
    } else if (strategy === 'join-avatar-matched' && wrapper) {
        el = joinAvatarForExpected(wrapper, expectedName, botName);
    } else if (strategy === 'join-avatar' && wrapper) {
        el = joinAvatarClickTarget(wrapper);
        const fold = foldImvuName(joinNameFromAvatar(wrapper));
        if (bot && fold === bot) return 'joiner-is-bot';
    } else if (strategy === 'roster-name') {
        el = findUserTarget(expectedName);
    } else if (strategy === 'participant') {
        el = findParticipantAvatarForName(expectedName);
    }
    if (!el) return 'no-el:' + strategy;
    if (!rightClickElement(el)) return 'click-failed:' + strategy;
    return 'clicked:' + strategy;
}
function joinAvatarClickTarget(wrapper) {
    // Join row: outer div (has uId) → first child div (contains <img>) — left-click that first child.
    const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
    if (kids.length < 1) return null;
    return kids[0];
}
/** In-page left-click only — no OS cursor, no right-click, no CDP. */
function leftClickEl(el) {
    if (!el) return false;
    try { return robustClick(el); } catch (e) { return false; }
}
/** Step 1: left-click first child div (image) on join row for this uId. */
function openJoinUserMenuByUid(joinRef, userId) {
    const wrapper = resolveJoinWrapper(joinRef, userId);
    if (!wrapper) return 'no-join-row';
    const firstDiv = joinAvatarClickTarget(wrapper);
    if (!firstDiv) return 'no-first-child';
    if (!firstDiv.querySelector('img') && !firstDiv.querySelector('[class*="avatar"]')) {
        // still click first child; structure may vary slightly
    }
    try { window._joinWhisperUserId = extractUserIdFromWrapper(wrapper) || userId || ''; } catch (e) {}
    if (!leftClickEl(firstDiv)) return 'click-failed';
    return 'ok';
}
/** Step 2: left-click exact menu item. */
function clickSendAWhisperExact() {
    for (const root of allSearchRoots()) {
        const item = root.querySelector('li[data-menu-item="send_a_whisper"]')
            || root.querySelector('[data-menu-item="send_a_whisper"]');
        if (!item) continue;
        if (!leftClickEl(item)) continue;
        return 'ok';
    }
    return 'no-menu-item';
}
function clickJoinAvatarForWhisper(joinRef, userId, expectedName, botName, useRightClick) {
    // useRightClick ignored — only left-click first child div
    return openJoinUserMenuByUid(joinRef, userId) === 'ok'
        ? ('avatar-clicked' + (userId ? ':uid=' + userId : ''))
        : 'no-join-row';
}
function clickJoinAvatarByRef(joinRef, expectedName, botName, useRightClick) {
    const row = findJoinRowByRef(joinRef);
    if (!row) return 'no-join-row';
    const wrapper = getJoinRowWrapper(row) || row;
    const joinerName = joinNameFromWrapper(wrapper);
    const joiner = normName(joinerName);
    const want = normName(expectedName);
    const bot = normName(botName || '');
    const avatarFold = foldImvuName(joinNameFromAvatar(wrapper));
    if (bot && avatarFold && avatarFold === bot) return 'joiner-is-bot';
    if (joiner && want && !namesRoughlyMatch(joiner, want)) return 'wrong-join-row:' + (joinerName || '?');
    if (bot && want && (namesRoughlyMatch(joiner, bot) || want === bot)) return 'joiner-is-bot';
    const clickTarget = joinAvatarClickTarget(wrapper);
    if (!clickTarget) return 'no-avatar';
    try { clickTarget.scrollIntoView({ block: 'nearest' }); } catch (e) {}
    if (useRightClick) {
        fireMouse(clickTarget, 'contextmenu', 2);
        fireMouse(clickTarget, 'mousedown', 2);
        fireMouse(clickTarget, 'mouseup', 2);
    } else {
        robustClick(clickTarget);
    }
    return useRightClick ? 'join-avatar-contextmenu' : 'join-avatar-clicked';
}
function clickParticipantAvatar(expectedName, botName, useRightClick) {
    const want = normName(expectedName);
    const bot = normName(botName || '');
    if (bot && want === bot) return 'joiner-is-bot';
    const el = findParticipantAvatarForName(expectedName);
    if (!el) return 'no-participant';
    if (useRightClick) fireMouse(el, 'contextmenu', 2);
    else fireMouse(el, 'click', 0);
    return useRightClick ? 'participant-contextmenu' : 'participant-clicked';
}
function pollProactiveWhisperState(expectedName, botName) {
    const verify = verifyWhisperTarget(expectedName, botName);
    if (verify === 'ok') return 'compose-ok';
    if (verify === 'target-is-bot' || verify === 'no-joiner-name') return verify;
    if (verify.startsWith('target-mismatch')) return verify;
    if (findSendAWhisperMenuItem('', '')) return 'menu-visible';
    if (whisperComposeOpen() === 'yes') return 'compose-unverified';
    return 'none';
}
function findJoinRowAvatarClick(targetName) {
    const want = normName(targetName);
    if (!want) return null;
    const root = __imvuFindChatRoot();
    const cont = root.cont;
    const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="join"], li, div');
    for (let i = rows.length - 1; i >= Math.max(0, rows.length - 80); i--) {
        const row = rows[i];
        const txt = (row.innerText || row.textContent || '').replace(/\s+/g, ' ').trim();
        if (!isJoinText(txt) || txt.length > 100) continue;
        let name = parseJoinName(txt);
        if (!name) name = getNameFromEl(row);
        if (normName(name) !== want) continue;
        const avatar = avatarFromJoinStructure(row);
        if (avatar) return avatar;
    }
    return null;
}
function pickClickable(node) {
    if (!node) return null;
    let best = node;
    for (let p = node, d = 0; p && d < 12; p = p.parentElement, d++) {
        const cls = (p.className || '').toString();
        const tag = (p.tagName || '').toLowerCase();
        if (/participant|avatar|user-row|member|presence|chat-user|room-user|profile|card/i.test(cls)) {
            best = p;
            break;
        }
        if (tag === 'li' || tag === 'button' || tag === 'a' || p.getAttribute('role') === 'button') {
            best = p;
        }
        if (p.querySelector && p.querySelector('img, [class*="avatar"]')) best = p;
    }
    return best;
}
function findUserTargetInDoc(doc, want) {
    for (const nameEl of doc.querySelectorAll('.cs2-name, [class*="cs2-name"], [class*="username"], [class*="display-name"]')) {
        if (isInsideWhisperCompose(nameEl)) continue;
        const n = foldImvuName(nameEl.textContent || nameEl.innerText);
        if (!namesRoughlyMatch(n, want)) continue;
        return pickClickable(nameEl);
    }
    const rosterSels = [
        '[class*="participant"]', '[class*="user-list"] li', '[class*="room-user"]',
        '[class*="chat-user"]', '[class*="avatar-list"] li', '[class*="members"] li',
        '[class*="user-row"]', '[class*="presence"]', '[class*="audience"] li'
    ];
    for (const rs of rosterSels) {
        for (const item of doc.querySelectorAll(rs)) {
            const name = normName(getNameFromEl(item));
            if (name !== want) continue;
            return pickClickable(item);
        }
    }
    return null;
}
function findUserTarget(targetName) {
    const avatar = findJoinRowAvatarClick(targetName);
    if (avatar) return avatar;
    const want = normName(targetName);
    if (!want) return null;
    for (const doc of __imvuAllDocs()) {
        const t = findUserTargetInDoc(doc, want);
        if (t) return t;
    }
    return null;
}
function allSearchRoots() {
    const roots = [];
    function add(root) {
        if (!root || roots.indexOf(root) >= 0) return;
        roots.push(root);
        try {
            const nodes = root.querySelectorAll ? root.querySelectorAll('*') : [];
            for (const el of nodes) {
                if (el.shadowRoot) add(el.shadowRoot);
            }
        } catch (e) {}
    }
    for (const doc of __imvuAllDocs()) add(doc);
    return roots;
}
function findVisibleMenus() {
    const menus = [];
    const sels = [
        '[role="menu"]', '[role="listbox"]', '[role="dialog"]',
        '[class*="context-menu"]', '[class*="dropdown-menu"]', '[class*="popup-menu"]',
        '[class*="user-menu"]', '[class*="profile-menu"]', '[class*="action-menu"]',
        '[class*="Popover"]', '[class*="popover"]', '[class*="Dropdown"]',
        '[class*="MenuList"]', '[class*="menu-list"]', '[class*="overlay-menu"]',
        '[class*="context-menu-manager"]', '[class*="menu-manager"]',
        'ul[class*="menu"]', '[class*="flyout"]', '[class*="tooltip-menu"]'
    ].join(',');
    for (const root of allSearchRoots()) {
        for (const el of root.querySelectorAll(sels)) {
            if (isVisibleEl(el) || (el.childElementCount > 0 && (el.textContent || '').trim().length > 0))
                menus.push(el);
        }
    }
    return menus;
}
function menuMatchesUserId(menu, userId) {
    if (!userId || !menu) return false;
    let node = menu;
    for (let d = 0; node && d < 10; d++) {
        const dataId = (node.getAttribute && node.getAttribute('data-id')) || '';
        if (dataId.includes('user-' + userId) || dataId.includes('user/user-' + userId)) return true;
        node = node.parentElement;
    }
    const blob = ((menu.innerHTML || '') + ' ' + (menu.textContent || ''));
    return blob.includes('user-' + userId);
}
function isWhisperActionText(txt) {
    txt = (txt || '').replace(/\s+/g, ' ').trim().toLowerCase();
    if (!txt || txt.length > 64) return false;
    if (txt === 'whisper' || txt === 'send whisper' || txt === 'send a whisper') return true;
    if (/^send\s+a?\s*whisper$/.test(txt)) return true;
    if (/\bwhisper\b/.test(txt) && !/reply/.test(txt)) return true;
    if (/\bwhisper\b/.test(txt) && /(send|private|message)/.test(txt)) return true;
    return false;
}
function elementOwnLabel(el) {
    if (!el || !el.getAttribute) return '';
    const al = (el.getAttribute('aria-label') || el.getAttribute('title') || el.getAttribute('data-tooltip') || '').trim();
    if (al) return al;
    let t = '';
    for (const n of el.childNodes) {
        if (n.nodeType === 3) t += n.textContent || '';
    }
    t = t.replace(/\s+/g, ' ').trim();
    if (t) return t;
    return (el.textContent || '').replace(/\s+/g, ' ').trim();
}
function pickWhisperClickable(el) {
    if (!el) return null;
    return el.closest('[data-menu-item], [role="menuitem"], [role="option"], button, a, [role="button"], li, [class*="menu-item"], [class*="MenuItem"]') || el;
}
function isReplyWhisperNoise(el) {
    const cls = ((el && el.className) || '').toString();
    return /reply_from_whisper|reply-to-whisper|whisper-reply|icon-reply/i.test(cls);
}
function whisperItemInMenu(menu) {
    if (!menu) return null;
    for (const el of menu.querySelectorAll('*')) {
        if (isReplyWhisperNoise(el)) continue;
        const dm = ((el.getAttribute && (el.getAttribute('data-menu-item') || '')) + ' ' +
            (el.getAttribute && (el.getAttribute('data-action') || '')) + ' ' +
            (el.getAttribute && (el.getAttribute('data-testid') || ''))).toLowerCase();
        if (dm.includes('whisper')) {
            const c = pickWhisperClickable(el);
            if (c && (isVisibleEl(c) || isVisibleEl(el))) return c;
        }
        const cls = (el.className || '').toString();
        if (/\bwhisper\b/i.test(cls) && !isReplyWhisperNoise(el)) {
            const c = pickWhisperClickable(el);
            if (c && isVisibleEl(c)) return c;
        }
        const label = elementOwnLabel(el);
        if (isWhisperActionText(label)) {
            const c = pickWhisperClickable(el);
            if (c) return c;
        }
    }
    return null;
}
function findWhisperActionAnywhere() {
    for (const root of allSearchRoots()) {
        for (const el of root.querySelectorAll('[data-menu-item], [data-action], [data-testid], [class*="whisper"], [class*="Whisper"], button, a, [role="menuitem"], [role="button"], li, span, div')) {
            if (isReplyWhisperNoise(el)) continue;
            const dm = ((el.getAttribute('data-menu-item') || '') + ' ' + (el.getAttribute('data-action') || '') + ' ' + (el.getAttribute('data-testid') || '')).toLowerCase();
            if (dm.includes('whisper') && !dm.includes('reply')) {
                const c = pickWhisperClickable(el);
                if (c && isVisibleEl(c)) return c;
            }
            const cls = (el.className || '').toString();
            if (/\bwhisper\b/i.test(cls) && !isReplyWhisperNoise(el) && isVisibleEl(el)) {
                const c = pickWhisperClickable(el);
                if (c) return c;
            }
            const label = elementOwnLabel(el);
            if (isWhisperActionText(label) && label.length <= 40) {
                const c = pickWhisperClickable(el);
                if (c && (isVisibleEl(c) || isVisibleEl(el))) return c;
            }
        }
    }
    return null;
}
function frameOffsetForEl(el) {
    let ox = 0, oy = 0;
    const win = el.ownerDocument?.defaultView;
    if (!win || win === window) return { ox, oy };
    if (win.frameElement) {
        const fr = win.frameElement.getBoundingClientRect();
        return { ox: fr.left, oy: fr.top };
    }
    for (const frame of document.querySelectorAll('iframe')) {
        try {
            if (frame.contentWindow === win) {
                const fr = frame.getBoundingClientRect();
                return { ox: fr.left, oy: fr.top };
            }
        } catch (e) {}
    }
    return { ox, oy };
}
function clickPointForEl(el) {
    if (!el) return '';
    try { el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) {}
    const r = el.getBoundingClientRect();
    if (r.width <= 0 || r.height <= 0) return '';
    const { ox, oy } = frameOffsetForEl(el);
    const x = Math.round(ox + r.left + Math.max(2, r.width / 2));
    const y = Math.round(oy + r.top + Math.max(2, r.height / 2));
    return x + ',' + y;
}
function getJoinAvatarClickPoint(joinRef, userId) {
    const wrapper = resolveJoinWrapper(joinRef, userId);
    if (!wrapper) return '';
    const el = joinAvatarClickTarget(wrapper);
    return clickPointForEl(el);
}
function getWhisperMenuClickPoint(userId, joinRef) {
    const item = findSendAWhisperMenuItem(userId, joinRef);
    return clickPointForEl(item);
}
function markJoinAvatarForClick(joinRef, userId) {
    const wrapper = resolveJoinWrapper(joinRef, userId);
    if (!wrapper) return 'no-join-row';
    const el = joinAvatarClickTarget(wrapper);
    if (!el) return 'no-avatar-button';
    const pt = clickPointForEl(el);
    if (!pt) return 'no-point';
    try { window._joinWhisperUserId = extractUserIdFromWrapper(wrapper) || userId || ''; } catch (e) {}
    return 'point:' + pt;
}
function clickUserTarget(targetName, useRightClick) {
    const el = findUserTarget(targetName);
    if (!el) return 'no-user-target';
    const fromJoinAvatar = !!findJoinRowAvatarClick(targetName);
    if (useRightClick) fireMouse(el, 'contextmenu', 2);
    else robustClick(el);
    if (fromJoinAvatar) return useRightClick ? 'join-avatar-contextmenu' : 'join-avatar-clicked';
    return useRightClick ? 'user-contextmenu' : 'user-clicked';
}
function getOpenUserMenuItems() {
    const items = [];
    const seen = new Set();
    for (const root of allSearchRoots()) {
        const menus = root.querySelectorAll(
            '[role="menu"], [class*="context-menu"], [class*="menu-manager"], [class*="user-menu"], [class*="action-menu"], [class*="dropdown-menu"]'
        );
        for (const menu of menus) {
            let cands = Array.from(menu.querySelectorAll('[role="menuitem"], [data-menu-item], [class*="menu-item"], [class*="MenuItem"]'));
            if (!cands.length) {
                cands = Array.from(menu.querySelectorAll('li, button, a, div, span')).filter(el => {
                    if (el.childElementCount > 6) return false;
                    const t = (elementOwnLabel(el) || (el.textContent || '')).replace(/\s+/g, ' ').trim();
                    return t.length >= 2 && t.length <= 48;
                });
            }
            for (const el of cands) {
                if (seen.has(el)) continue;
                const r = el.getBoundingClientRect();
                if (r.width <= 0 || r.height <= 0) continue;
                const label = (elementOwnLabel(el) || (el.textContent || '')).replace(/\s+/g, ' ').trim();
                if (!label || label.length > 48) continue;
                seen.add(el);
                items.push({ el, label, y: r.top });
            }
        }
    }
    items.sort((a, b) => a.y - b.y);
    return items;
}
function findSendAWhisperMenuItem(userId, joinRef) {
    for (const root of allSearchRoots()) {
        const exact = root.querySelector('li[data-menu-item="send_a_whisper"], [data-menu-item="send_a_whisper"]');
        if (exact) return exact;
    }
    return null;
}
function getMenuItemsDebug() {
    const items = getOpenUserMenuItems();
    if (!items.length) return 'menu-items=0';
    return 'menu-items=' + items.length + ' | ' + items.map((it, i) => (i + 1) + ':' + it.label).join(' || ');
}
function whisperMenuDebug() {
    const itemsDbg = getMenuItemsDebug();
    const any = findSendAWhisperMenuItem('', '');
    const pick = any ? ((elementOwnLabel(any) || any.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 40)) : 'none';
    return itemsDbg + ' | pick=' + pick;
}
function clickSendAWhisperMenu(userId, joinRef) {
    return clickSendAWhisperExact() === 'ok' ? 'menu-clicked' : 'no-menu-item';
}
function whisperComposeOpen() {
    return anyWhisperComposeActive() ? 'yes' : 'no';
}
function dismissOpenUi() {
    for (const doc of __imvuAllDocs()) {
        try {
            doc.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
            doc.dispatchEvent(new KeyboardEvent('keyup', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
        } catch (e) {}
        const inp = doc.querySelector('div.input-container textarea, div.input-container input, div.input-container [contenteditable]');
        if (inp) {
            try {
                inp.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
                inp.blur();
            } catch (e) {}
        }
    }
    return 'dismissed';
}
function proactiveWhisperReady(expectedName, botName, trustJoinMenu, trustUserId) {
    const v = verifyWhisperTarget(expectedName, botName, !!trustJoinMenu, trustUserId || '');
    if (v === 'ok' || v === 'ok-trusted') return 'ok';
    return v;
}
/* ===== Silent whisper via IMVU activeChat API (no menus / no mouse) =====
 * From welcome.min.js: menu item "send_a_whisper" calls
 *   activeChat.handleWhisperAttempt(node)
 * Chat/gifts use:
 *   activeChat.sendMessage(text)
 * Whisper mode uses messageTarget; reset with resetMessageTarget().
 */
function __imvuAllWindows() {
    const wins = [];
    const seen = new Set();
    function addWin(w) {
        if (!w || seen.has(w)) return;
        seen.add(w);
        wins.push(w);
        try {
            for (const f of w.document.querySelectorAll('iframe')) {
                try { if (f.contentWindow) addWin(f.contentWindow); } catch (e) {}
            }
        } catch (e) {}
    }
    addWin(window);
    try { if (window.top && window.top !== window) addWin(window.top); } catch (e) {}
    return wins;
}
function __getCachedActiveChat() {
    for (const w of __imvuAllWindows()) {
        try {
            if (w.__imvuCompanionActiveChat) return w.__imvuCompanionActiveChat;
        } catch (e) {}
    }
    try {
        if (window.top && window.top.__imvuCompanionActiveChat)
            return window.top.__imvuCompanionActiveChat;
    } catch (e) {}
    return null;
}
function __chatMethodScore(o) {
    if (!o || typeof o !== 'object') return -1;
    let score = 0;
    try {
        if (typeof o.handleWhisperAttempt === 'function') score += 100;
        if (typeof o.sendMessage === 'function') score += 40;
        if (typeof o.resetMessageTarget === 'function') score += 20;
        if (typeof o.inWhisperMode === 'function') score += 15;
        if (typeof o.set === 'function') score += 15;
        if (typeof o.get === 'function') score += 5;
        if (typeof o.trigger === 'function') score += 5;
        if (typeof o.getParticipants === 'function') score += 5;
    } catch (e) {}
    return score;
}
function __chatMethodList(o) {
    const names = ['handleWhisperAttempt', 'sendMessage', 'resetMessageTarget', 'inWhisperMode', 'set', 'get', 'trigger', 'getParticipants'];
    const have = [];
    for (const n of names) {
        try { if (typeof o[n] === 'function') have.push(n); } catch (e) {}
    }
    return have.join(',');
}
function __cacheActiveChat(o) {
    if (!o) return;
    try { window.__imvuCompanionActiveChat = o; } catch (e) {}
    try { if (window.top) window.top.__imvuCompanionActiveChat = o; } catch (e) {}
}
/** Prefer object that can actually whisper (handleWhisperAttempt + sendMessage). */
function __findActiveChat() {
    const seen = new Set();
    const q = [];
    let best = null;
    let bestScore = -1;

    function consider(o) {
        if (!o || (typeof o !== 'object' && typeof o !== 'function')) return;
        try {
            if (seen.has(o)) return;
            seen.add(o);
        } catch (e) { return; }
        q.push(o);
        const score = __chatMethodScore(o);
        // Must be able to send somehow; prefer whisper-capable
        if (score >= 40 && score > bestScore) {
            bestScore = score;
            best = o;
        }
    }

    // Cached only if it still looks good
    try {
        const cached = __getCachedActiveChat();
        if (cached && __chatMethodScore(cached) >= 100) return cached;
        if (cached) consider(cached);
    } catch (e) {}

    for (const w of __imvuAllWindows()) {
        try { consider(w.IMVU); consider(w.$); consider(w.jQuery); } catch (e) {}
        try {
            for (const k of Object.getOwnPropertyNames(w)) {
                try {
                    const v = w[k];
                    consider(v);
                    if (v && typeof v.get === 'function') {
                        try { consider(v.get('activeChat')); } catch (e) {}
                        try { consider(v.get('chat')); } catch (e) {}
                        try { consider(v.get('policyChat')); } catch (e) {}
                    }
                } catch (e) {}
            }
        } catch (e) {}
        try {
            const doc = w.document;
            if (!doc) continue;
            const $ = w.jQuery || w.$;
            for (const el of doc.querySelectorAll('.btn-send, [class*="chat-bar"], [class*="input-container"], [class*="chat-stream"]')) {
                try { consider(el.__view); consider(el._view); consider(el.__backboneView); } catch (e) {}
                if ($) {
                    try {
                        const d = $(el).data();
                        if (d) for (const v of Object.values(d)) consider(v);
                    } catch (e) {}
                }
                // views hold __activeChat
                let p = el;
                for (let d = 0; p && d < 10; d++, p = p.parentElement) {
                    try {
                        for (const key of Object.keys(p)) {
                            if (/activeChat|chat|view|context/i.test(key) || key.startsWith('__')) {
                                try { consider(p[key]); } catch (e) {}
                            }
                        }
                    } catch (e) {}
                }
            }
        } catch (e) {}
    }

    let guard = 0;
    while (q.length && guard++ < 10000) {
        const o = q.shift();
        try {
            // Perfect match — stop early
            if (typeof o.handleWhisperAttempt === 'function' && typeof o.sendMessage === 'function') {
                __cacheActiveChat(o);
                return o;
            }
            if (o.__activeChat) consider(o.__activeChat);
            if (o.__serviceProvider) consider(o.__serviceProvider);
            if (o.serviceProvider) consider(o.serviceProvider);
            if (typeof o.get === 'function' && typeof o.register === 'function') {
                try { consider(o.get('activeChat')); } catch (e) {}
            }
            if (guard < 6000) {
                let keys = [];
                try { keys = Object.keys(o); } catch (e) {
                    try { keys = Object.getOwnPropertyNames(o); } catch (e2) {}
                }
                for (const k of keys.slice(0, 100)) {
                    if (/chat|service|provider|participant|messageTarget|manager|context|room|scene|policy/i.test(k) || k.startsWith('__')) {
                        try { consider(o[k]); } catch (e) {}
                    }
                }
            }
        } catch (e) {}
    }

    if (best) __cacheActiveChat(best);
    return best;
}
function __nodeCid(node) {
    if (!node) return '';
    try {
        if (typeof node.get === 'function') {
            const a = node.get('legacy_cid');
            if (a != null && a !== '') return String(a);
            const b = node.get('cid');
            if (b != null && b !== '') return String(b);
        }
    } catch (e) {}
    try {
        if (node.legacy_cid != null) return String(node.legacy_cid);
        if (node.cid != null) return String(node.cid);
        if (node.attributes) {
            if (node.attributes.legacy_cid != null) return String(node.attributes.legacy_cid);
            if (node.attributes.cid != null) return String(node.attributes.cid);
        }
    } catch (e) {}
    return '';
}
function __nodeId(node) {
    if (!node) return '';
    try {
        if (typeof node.get === 'function') {
            const id = node.get('id');
            if (id != null) return String(id);
        }
    } catch (e) {}
    try { return String(node.id || (node.attributes && node.attributes.id) || ''); } catch (e) { return ''; }
}
function __nodeDisplayName(node) {
    if (!node) return '';
    try {
        if (typeof node.get === 'function') {
            const n = node.get('display_name') || node.get('username') || node.get('name');
            if (n) return String(n);
        }
    } catch (e) {}
    try {
        return String(node.display_name || node.username || (node.attributes && node.attributes.display_name) || '');
    } catch (e) { return ''; }
}
function __cidMatches(uid, node) {
    const u = String(uid || '').trim();
    if (!u || !node) return false;
    const cid = __nodeCid(node);
    if (cid && cid === u) return true;
    const id = __nodeId(node);
    if (!id) return false;
    if (id === u) return true;
    if (id.includes('user-' + u)) return true;
    if (id.endsWith('/' + u) || id.endsWith('-' + u)) return true;
    // api.imvu.com/user/user-12345
    const m = id.match(/user[_/-](\d+)/i);
    if (m && m[1] === u) return true;
    return false;
}
function __takeModels(coll, out) {
    if (!coll || !out) return;
    try {
        if (coll.models && coll.models.length != null) {
            for (const m of coll.models) out.push(m);
            return;
        }
        if (Array.isArray(coll)) {
            for (const m of coll) out.push(m);
            return;
        }
        if (typeof coll.each === 'function') {
            coll.each(function (m) { out.push(m); });
            return;
        }
        if (typeof coll.forEach === 'function') coll.forEach(function (m) { out.push(m); });
    } catch (e) {}
}
/** Live rooms: getParticipants() returns an EMPTY collection. Real list is __participants on policy/scene. */
function __chatRelatedRoots(chat) {
    const roots = [];
    const seen = new Set();
    function add(o) {
        if (!o || typeof o !== 'object') return;
        try { if (seen.has(o)) return; seen.add(o); roots.push(o); } catch (e) {}
    }
    add(chat);
    try { add(chat.__policyChat); } catch (e) {}
    try { if (typeof chat._getPolicy === 'function') add(chat._getPolicy()); } catch (e) {}
    try { add(chat.__chatScene); } catch (e) {}
    try { if (typeof chat.getScene === 'function') add(chat.getScene()); } catch (e) {}
    try { add(chat.chatModel); } catch (e) {}
    try { add(chat.__roomModel); } catch (e) {}
    try {
        for (const k of Object.keys(chat)) {
            try {
                const v = chat[k];
                if (!v || typeof v !== 'object') continue;
                if (v.__participants || typeof v.__getParticipantNodeByLegacyCid === 'function' ||
                    typeof v.__getParticipantNodeByKey === 'function' || v.chatModel)
                    add(v);
            } catch (e) {}
        }
    } catch (e) {}
    return roots;
}
function __participantModels(chat) {
    const out = [];
    const seen = new Set();
    function pushAll(coll) {
        const tmp = [];
        __takeModels(coll, tmp);
        for (const m of tmp) {
            try {
                if (seen.has(m)) continue;
                seen.add(m);
                out.push(m);
            } catch (e) { out.push(m); }
        }
    }
    for (const root of __chatRelatedRoots(chat)) {
        try { pushAll(root.__participants); } catch (e) {}
        try { pushAll(root.participants); } catch (e) {}
        try { pushAll(root.__userCollection); } catch (e) {}
        try { pushAll(root.__participantsCollection); } catch (e) {}
        try {
            if (typeof root.getParticipants === 'function') {
                const p = root.getParticipants();
                // ignore empty placeholder collections and promises here
                if (p && typeof p.then !== 'function') {
                    const n = p.models ? p.models.length : 0;
                    if (n > 0) pushAll(p);
                }
            }
        } catch (e) {}
        try { if (root.get) pushAll(root.get('participants')); } catch (e) {}
    }
    return out;
}
function __findParticipantHelpers() {
    const found = [];
    const seen = new Set();
    function add(o) {
        if (!o || typeof o !== 'object') return;
        try {
            if (seen.has(o)) return;
            seen.add(o);
        } catch (e) { return; }
        try {
            if (typeof o.__getParticipantNodeByLegacyCid === 'function' ||
                typeof o.__getParticipantNodeByKey === 'function' ||
                (o.__participants && o.__participants.models))
                found.push(o);
        } catch (e) {}
    }
    for (const w of __imvuAllWindows()) {
        try {
            for (const k of Object.getOwnPropertyNames(w)) {
                try { add(w[k]); } catch (e) {}
            }
        } catch (e) {}
        try {
            const doc = w.document;
            if (!doc) continue;
            for (const el of doc.querySelectorAll('[class*="chat"], [class*="scene"], [class*="room"], .btn-send')) {
                try {
                    add(el.__view); add(el._view);
                    if (w.jQuery || w.$) {
                        const $ = w.jQuery || w.$;
                        const d = $(el).data();
                        if (d) for (const v of Object.values(d)) add(v);
                    }
                } catch (e) {}
            }
        } catch (e) {}
    }
    return found;
}
/** Deep-scan object graph for participant collections / lookup helpers. */
function __deepFindParticipantSources(root, maxDepth) {
    const helpers = [];
    const collections = [];
    const seen = new Set();
    const limit = maxDepth || 6;
    function walk(o, depth) {
        if (!o || depth > limit) return;
        if (typeof o !== 'object' && typeof o !== 'function') return;
        try {
            if (seen.has(o)) return;
            seen.add(o);
        } catch (e) { return; }
        try {
            if (typeof o.__getParticipantNodeByLegacyCid === 'function' ||
                typeof o.__getParticipantNodeByKey === 'function')
                helpers.push(o);
        } catch (e) {}
        try {
            if (o.__participants && o.__participants.models && o.__participants.models.length > 0)
                collections.push(o.__participants);
            // collection of participant models directly
            if (o.models && o.models.length > 0 && o.models[0] && (o.models[0].node || o.models[0].attributes))
                collections.push(o);
        } catch (e) {}
        if (depth >= limit) return;
        let keys = [];
        try { keys = Object.keys(o); } catch (e) {
            try { keys = Object.getOwnPropertyNames(o); } catch (e2) { return; }
        }
        for (const k of keys) {
            if (k === 'el' || k === '$el' || k === 'window' || k === 'document' || k === 'parent' || k === 'top') continue;
            try { walk(o[k], depth + 1); } catch (e) {}
        }
    }
    walk(root, 0);
    return { helpers: helpers, collections: collections };
}
function __nodeFromParticipantModel(m) {
    if (!m) return null;
    try {
        if (m.node) return m.node;
        if (typeof m.get === 'function') {
            const n = m.get('node');
            if (n) return n;
        }
    } catch (e) {}
    return m;
}
function __matchNodeInCollection(coll, uidStr) {
    if (!coll) return null;
    const models = coll.models || (Array.isArray(coll) ? coll : []);
    for (const m of models) {
        try {
            const node = __nodeFromParticipantModel(m);
            if (!node) continue;
            const attrs = node.attributes || {};
            let cid = attrs.legacy_cid;
            if (cid == null && typeof node.get === 'function') {
                try { cid = node.get('legacy_cid'); } catch (e) {}
            }
            if (cid != null && String(cid) === uidStr) return node;
            if (__cidMatches(uidStr, node)) return node;
            // also match on edge participant wrappers
            try {
                if (m.edge && m.edge.node) {
                    const en = m.edge.node;
                    if (__cidMatches(uidStr, en)) return en;
                }
            } catch (e) {}
        } catch (e) {}
    }
    return null;
}
/** Minimal Backbone-like user node so handleWhisperAttempt can target a cid without a list hit. */
function __syntheticUserNode(userId, displayName) {
    const uidNum = parseInt(String(userId || ''), 10);
    const uidStr = String(userId || '').trim();
    const id = 'https://api.imvu.com/user/user-' + uidStr;
    const attrs = {
        legacy_cid: isNaN(uidNum) ? uidStr : uidNum,
        display_name: displayName || ('user' + uidStr),
        id: id
    };
    return {
        id: id,
        attributes: attrs,
        get: function (key) {
            if (key === 'id') return this.id;
            return this.attributes[key];
        },
        toJSON: function () { return Object.assign({ id: this.id }, this.attributes); }
    };
}
/** Prefer IMVU helpers that return participant by legacy_cid (async). */
async function __resolveParticipantNode(chat, userId, displayName) {
    const uidNum = parseInt(String(userId || ''), 10);
    const uidStr = String(userId || '').trim();

    // 1) Deep search from activeChat + window-level helpers
    const sources = __deepFindParticipantSources(chat, 7);
    for (const h of __findParticipantHelpers()) sources.helpers.push(h);
    for (const root of __chatRelatedRoots(chat)) {
        const more = __deepFindParticipantSources(root, 5);
        for (const h of more.helpers) sources.helpers.push(h);
        for (const c of more.collections) sources.collections.push(c);
    }
    // also deep-scan a few global window objects
    for (const w of __imvuAllWindows()) {
        try {
            if (w.IMVU) {
                const more = __deepFindParticipantSources(w.IMVU, 4);
                for (const h of more.helpers) sources.helpers.push(h);
                for (const c of more.collections) sources.collections.push(c);
            }
        } catch (e) {}
    }

    for (const root of sources.helpers) {
        try {
            if (typeof root.__getParticipantNodeByLegacyCid === 'function' && !isNaN(uidNum)) {
                const part = await root.__getParticipantNodeByLegacyCid(uidNum);
                if (part) {
                    if (part.node) return part.node;
                    if (typeof part.get === 'function' || part.attributes) return part;
                }
            }
        } catch (e) {}
        try {
            if (typeof root.__getParticipantNodeByKey === 'function' && !isNaN(uidNum)) {
                const part = await root.__getParticipantNodeByKey('legacy_cid', uidNum);
                if (part && part.node) return part.node;
                if (part) return part;
            }
        } catch (e) {}
    }

    for (const coll of sources.collections) {
        const hit = __matchNodeInCollection(coll, uidStr);
        if (hit) return hit;
    }

    // Edge collection on chatModel (classic rooms)
    for (const root of __chatRelatedRoots(chat)) {
        try {
            const model = root.chatModel || root.__chatModel;
            if (model && typeof model.getEdgeCollection === 'function') {
                let coll = model.getEdgeCollection('participants');
                if (coll && typeof coll.populated === 'function') coll = await coll.populated();
                const hit = __matchNodeInCollection(coll, uidStr);
                if (hit) return hit;
            }
        } catch (e) {}
    }

    // Sync scan
    const sync = __participantNodeByCid(chat, userId, displayName);
    if (sync) return sync;

    // Last resort: synthetic node from join uId + name (same fields handleWhisperAttempt reads)
    if (uidStr) return __syntheticUserNode(uidStr, displayName);
    return null;
}
function __nodeFromModel(m) {
    if (!m) return null;
    try {
        if (m.node) return m.node;
        if (typeof m.get === 'function') {
            const n = m.get('node');
            if (n) return n;
        }
    } catch (e) {}
    // model itself may be the user node
    if (typeof m.get === 'function' && (__nodeCid(m) || __nodeId(m))) return m;
    return m;
}
function __participantNodeByCid(chat, userId, displayName) {
    const uid = String(userId || '').trim();
    const wantName = foldImvuName(displayName || '');
    if (!chat) return null;

    const models = __participantModels(chat);
    for (const m of models) {
        try {
            const node = __nodeFromModel(m);
            if (!node) continue;
            if (uid && __cidMatches(uid, node)) return node;
        } catch (e) {}
    }
    // Fallback: display name (joiners rename often — only if unique-ish match)
    if (wantName) {
        let hit = null, hits = 0;
        for (const m of models) {
            try {
                const node = __nodeFromModel(m);
                const nm = foldImvuName(__nodeDisplayName(node));
                if (nm && (nm === wantName || nm.includes(wantName) || wantName.includes(nm))) {
                    hit = node; hits++;
                }
            } catch (e) {}
        }
        if (hits === 1) return hit;
    }
    try {
        if (typeof chat.getParticipant === 'function') {
            const n = chat.getParticipant(uid) || chat.getParticipant(Number(uid));
            if (n) return n.node || n;
        }
    } catch (e) {}
    return null;
}
function __listParticipantCids(chat, limit) {
    const lim = limit || 12;
    const rows = [];
    for (const m of __participantModels(chat).slice(0, lim)) {
        try {
            const node = __nodeFromModel(m);
            rows.push(__nodeCid(node) + ':' + __nodeDisplayName(node).slice(0, 24) + ' id=' + __nodeId(node).slice(-40));
        } catch (e) {}
    }
    return 'n=' + __participantModels(chat).length + ' sample=[' + rows.join(' | ') + ']';
}
function __findChatBar(chat) {
    const seen = new Set();
    const q = [];
    let best = null;
    function add(o) {
        if (!o || typeof o !== 'object') return;
        try { if (seen.has(o)) return; seen.add(o); q.push(o); } catch (e) {}
    }
    function scoreBar(o) {
        let s = 0;
        try {
            if (typeof o.__send === 'function') s += 50;
            if (typeof o.set === 'function') s += 20;
            if (typeof o.get === 'function') s += 10;
            if (o.__textarea) s += 20;
            if (o.__activeChat === chat) s += 40;
            if (o.className === 'chat-bar' || (o.el && /chat-bar/i.test(o.el.className || ''))) s += 15;
            if (o.uiContextName && /chat_bar/i.test(o.uiContextName)) s += 25;
        } catch (e) {}
        return s;
    }
    add(chat);
    for (const w of __imvuAllWindows()) {
        try {
            const $ = w.jQuery || w.$;
            for (const el of w.document.querySelectorAll(
                '.chat-bar, [class*="chat-bar"], .btn-send, .btn-send.whisper, textarea.input-text, .input-text, [class*="input-container"], .whisper-close, [class*="whisper-cancel"]'
            )) {
                try {
                    add(el.__view); add(el._view); add(el.__backboneView); add(el.view);
                    // walk element own props for view refs
                    for (const k of Object.keys(el)) {
                        if (k.length > 48) continue;
                        try { add(el[k]); } catch (e) {}
                    }
                    if ($) {
                        try {
                            const d = $(el).data();
                            if (d) for (const v of Object.values(d)) add(v);
                            // some builds store view on closest chat-bar
                            const $bar = $(el).closest('.chat-bar, [class*="chat-bar"]');
                            if ($bar.length) {
                                const bd = $bar.data();
                                if (bd) for (const v of Object.values(bd)) add(v);
                            }
                        } catch (e) {}
                    }
                } catch (e) {}
            }
        } catch (e) {}
    }
    let guard = 0;
    let bestScore = 0;
    while (q.length && guard++ < 6000) {
        const o = q.shift();
        try {
            const sc = scoreBar(o);
            if (sc >= 50 && sc > bestScore) {
                bestScore = sc;
                best = o;
                if (sc >= 100) return o;
            }
            if (o.__activeChat) add(o.__activeChat);
            if (o.__serviceProvider) add(o.__serviceProvider);
            if (o.el) add(o.el);
            if (o.$el && o.$el[0]) add(o.$el[0]);
            for (const k of Object.keys(o).slice(0, 50)) {
                if (/bar|footer|input|chat|menu|context|textarea|send/i.test(k) || k.startsWith('__')) {
                    try { add(o[k]); } catch (e) {}
                }
            }
        } catch (e) {}
    }
    return best;
}
/** When Backbone view is not found: type into open whisper bar DOM and submit. */
function __domWhisperUi() {
    for (const w of __imvuAllWindows()) {
        try {
            const doc = w.document;
            const close = doc.querySelector(
                '.whisper-close, span.whisper-close, [class*="whisper-close"], .whisper-cancel-bar, [class*="whisper-cancel"]'
            );
            let root = null;
            if (close) {
                root = close.closest('.chat-bar') ||
                    close.closest('[class*="chat-bar"]') ||
                    close.closest('[class*="input-container"]') ||
                    close.parentElement;
            }
            if (!root) root = doc.querySelector('.chat-bar, [class*="chat-bar"]');
            let ta = root ? root.querySelector('textarea, input:not([type="hidden"]), [contenteditable="true"]') : null;
            if (!ta) ta = doc.querySelector('textarea.input-text, .chat-bar textarea, [class*="chat-bar"] textarea');
            let btn = root ? root.querySelector('.btn-send, button.btn-send, button[type="submit"]') : null;
            if (!btn) btn = doc.querySelector('.btn-send.whisper, .chat-bar .btn-send, button.btn-send');
            const closeBtn = close ||
                (root && root.querySelector('.whisper-close, [class*="whisper-close"]')) ||
                doc.querySelector('.whisper-close, [class*="whisper-close"]');
            if (ta || btn) {
                return { win: w, doc: doc, root: root, ta: ta, btn: btn, closeBtn: closeBtn };
            }
        } catch (e) {}
    }
    return null;
}
function __setInputValue(el, text) {
    if (!el) return false;
    try { el.focus(); } catch (e) {}
    try {
        if (el.isContentEditable) {
            el.textContent = text;
        } else {
            const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
            const desc = Object.getOwnPropertyDescriptor(proto, 'value');
            if (desc && desc.set) desc.set.call(el, text);
            else el.value = text;
        }
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
        try {
            el.dispatchEvent(new InputEvent('input', { bubbles: true, data: text, inputType: 'insertText' }));
        } catch (e) {}
        return true;
    } catch (e) { return false; }
}
function __pressEnter(el) {
    if (!el) return;
    const opts = { key: 'Enter', code: 'Enter', keyCode: 13, which: 13, bubbles: true, cancelable: true };
    try { el.dispatchEvent(new KeyboardEvent('keydown', opts)); } catch (e) {}
    try { el.dispatchEvent(new KeyboardEvent('keypress', opts)); } catch (e) {}
    try { el.dispatchEvent(new KeyboardEvent('keyup', opts)); } catch (e) {}
}
function __domSendWhisperText(text) {
    const ui = __domWhisperUi();
    if (!ui || !ui.ta) return 'no-dom-input';
    if (!__setInputValue(ui.ta, text)) return 'dom-set-failed';
    // ExpandingChatBar listens for Enter on the textarea
    __pressEnter(ui.ta);
    if (ui.btn) {
        try {
            ui.btn.disabled = false;
            ui.btn.removeAttribute('disabled');
        } catch (e) {}
        try { ui.btn.click(); } catch (e) {}
    }
    // form submit fallback
    try {
        const form = ui.ta.closest('form');
        if (form && form.requestSubmit) form.requestSubmit();
    } catch (e) {}
    return 'ok-dom';
}
function __domCloseWhisperBar() {
    const ui = __domWhisperUi();
    if (ui && ui.closeBtn) {
        try { ui.closeBtn.click(); return 'closed-dom'; } catch (e) {}
    }
    for (const w of __imvuAllWindows()) {
        try {
            const doc = w.document;
            for (const el of doc.querySelectorAll(
                '.whisper-close, span.whisper-close, [class*="whisper-close"], [class*="whisper-cancel"] button, [class*="whisper-cancel"]'
            )) {
                try {
                    const r = el.getBoundingClientRect();
                    if (r.width > 0 && r.height > 0) {
                        el.click();
                        return 'closed-dom';
                    }
                } catch (e) {}
            }
            // Escape on input
            const ta = doc.querySelector('textarea.input-text, .chat-bar textarea');
            if (ta) {
                ta.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', keyCode: 27, bubbles: true }));
            }
        } catch (e) {}
    }
    return 'close-miss';
}
function __isInWhisperMode(chat) {
    if (!chat) return false;
    try {
        if (typeof chat.inWhisperMode === 'function') return !!chat.inWhisperMode();
    } catch (e) {}
    try {
        if (typeof chat.get === 'function') {
            const t = chat.get('messageTarget');
            if (t && (t.cid != null || t.node || t.displayName)) return true;
        }
    } catch (e) {}
    try {
        const t = chat.attributes && chat.attributes.messageTarget;
        if (t && (t.cid != null || t.node)) return true;
    } catch (e) {}
    try {
        if (chat.messageTarget && (chat.messageTarget.cid != null || chat.messageTarget.node)) return true;
    } catch (e) {}
    // DOM: whisper bar open
    try {
        if (__domWhisperUi() && (__domWhisperUi().closeBtn || document.querySelector('.btn-send.whisper, [class*="whisper-cancel"]')))
            return true;
    } catch (e) {}
    return false;
}
function __delay(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
}
/**
 * Live rooms: getParticipants() is empty; use __participants / __getParticipantNodeByLegacyCid.
 * Result is always written to window.__imvuWhisperResult as a plain string (WebView2
 * often serializes Promise results as {} — so we never return a Promise to the host).
 */
async function silentWhisperTryOnce(userId, text, displayName) {
    try {
        const msg = String(text || '');
        if (!msg) return 'empty-message';
        const chat = __findActiveChat();
        if (!chat) return 'no-active-chat';

        // Re-resolve chat preferring handleWhisperAttempt (cached object may be incomplete).
        let chatObj = chat;
        if (typeof chatObj.handleWhisperAttempt !== 'function') {
            // Clear bad cache and search again
            try { window.__imvuCompanionActiveChat = null; } catch (e) {}
            try { if (window.top) window.top.__imvuCompanionActiveChat = null; } catch (e) {}
            const better = __findActiveChat();
            if (better) chatObj = better;
        }

        const methods = __chatMethodList(chatObj);
        let node = await __resolveParticipantNode(chatObj, userId, displayName);
        if (!node) {
            return 'no-participant:' + (userId || '?') + ' ' + __listParticipantCids(chatObj, 8) + ' methods=[' + methods + ']';
        }

        const cid = Number(userId) || userId;
        const dn = displayName || __nodeDisplayName(node) || '';
        const target = { cid: cid, displayName: dn, node: node };
        let modeHow = '';

        // Open whisper mode (menu path). Do NOT use sendMessage — that is public/system chat.
        if (typeof chatObj.handleWhisperAttempt === 'function') {
            try {
                const r = chatObj.handleWhisperAttempt(node);
                if (r && typeof r.then === 'function') await r;
                modeHow = 'handleWhisperAttempt';
            } catch (e) {
                modeHow = 'handleWhisperAttempt-err:' + (e && e.message ? e.message : e);
            }
        }
        if (!__isInWhisperMode(chatObj) && typeof chatObj.set === 'function') {
            try {
                chatObj.set('messageTarget', target);
                if (typeof chatObj.trigger === 'function') {
                    chatObj.trigger('change:messageTarget', chatObj, target);
                    chatObj.trigger('startWhisper');
                }
                modeHow = modeHow ? modeHow + '+set' : 'set-messageTarget';
            } catch (e) {
                return 'whisper-target-error:' + (e && e.message ? e.message : String(e));
            }
        }

        // Wait briefly for whisper mode / UI to apply
        for (let w = 0; w < 10; w++) {
            if (__isInWhisperMode(chatObj)) break;
            await __delay(50);
        }

        // Wait for whisper bar UI (mode may be set before DOM updates)
        for (let w = 0; w < 15; w++) {
            if (__isInWhisperMode(chatObj) || __domWhisperUi()) break;
            await __delay(80);
        }

        if (!__isInWhisperMode(chatObj) && !__domWhisperUi()) {
            return 'whisper-mode-not-active methods=[' + methods + '] how=' + modeHow;
        }

        // Send: prefer ExpandingChatBar view; fallback to open whisper-bar DOM (type + Enter + close).
        // Never use activeChat.sendMessage() — that posts public/system lines.
        let sendHow = '';
        const bar = __findChatBar(chatObj);
        if (bar && typeof bar.__send === 'function') {
            try {
                if (typeof bar.set === 'function') bar.set(msg);
                else if (bar.__textarea) {
                    try {
                        if (bar.__textarea.val) bar.__textarea.val(msg);
                        else if (bar.__textarea[0]) __setInputValue(bar.__textarea[0], msg);
                    } catch (e) {}
                }
                bar.__send({ preventDefault: function () {}, keyCode: 13 });
                sendHow = 'bar-__send';
            } catch (e) {
                sendHow = 'bar-err:' + (e && e.message ? e.message : e);
            }
        }

        if (!sendHow || sendHow.indexOf('err') >= 0) {
            if (bar && typeof bar.trigger === 'function') {
                try {
                    bar.trigger('sendInput', { message: msg });
                    sendHow = 'bar-sendInput';
                } catch (e) {}
            }
        }

        if (!sendHow || sendHow.indexOf('err') >= 0) {
            const dom = __domSendWhisperText(msg);
            if (dom === 'ok-dom') sendHow = 'dom';
            else return 'no-chat-bar mode-ok how=' + modeHow + ' dom=' + dom + ' methods=[' + methods + ']';
        }

        await __delay(350);

        // Close whisper bar (X) — API + DOM
        try {
            if (typeof chatObj.resetMessageTarget === 'function') chatObj.resetMessageTarget();
        } catch (e) {}
        __domCloseWhisperBar();
        await __delay(100);
        try {
            if (typeof chatObj.resetMessageTarget === 'function') chatObj.resetMessageTarget();
        } catch (e) {}

        return 'ok:' + modeHow + '+' + sendHow;
    } catch (e) {
        return 'exception:' + (e && e.message ? e.message : String(e));
    }
}
/** Fire-and-forget entry: host polls window.__imvuWhisperResult */
function silentWhisperStart(userId, text, displayName) {
    try {
        window.__imvuWhisperResult = 'pending';
        Promise.resolve()
            .then(function () { return silentWhisperTryOnce(userId, text, displayName); })
            .then(function (r) {
                window.__imvuWhisperResult = (r == null || r === '') ? 'empty-result' : String(r);
            })
            .catch(function (e) {
                window.__imvuWhisperResult = 'exception:' + (e && e.message ? e.message : String(e));
            });
        return 'started';
    } catch (e) {
        window.__imvuWhisperResult = 'exception:' + (e && e.message ? e.message : String(e));
        return 'started';
    }
}
function silentWhisperPoll() {
    try {
        const r = window.__imvuWhisperResult;
        if (r == null || r === '') return 'pending';
        return String(r);
    } catch (e) {
        return 'pending';
    }
}
function silentWhisperProbe() {
    const cached = __getCachedActiveChat();
    const chat = cached || __findActiveChat();
    if (!chat) {
        let spHits = 0;
        for (const w of __imvuAllWindows()) {
            try {
                for (const k of Object.getOwnPropertyNames(w)) {
                    try {
                        const v = w[k];
                        if (v && typeof v.get === 'function' && typeof v.register === 'function') spHits++;
                    } catch (e) {}
                }
            } catch (e) {}
        }
        return 'no-active-chat frames=' + __imvuAllWindows().length + ' serviceProviders~' + spHits +
            ' hook=' + (window.__imvuCompanionHooksInstalled ? 'yes' : 'no');
    }
    const methods = [];
    for (const k of ['handleWhisperAttempt', 'sendMessage', 'resetMessageTarget', 'getParticipants', 'inWhisperMode', 'set',
        '__getParticipantNodeByLegacyCid', '__participants']) {
        if (typeof chat[k] === 'function' || (k.startsWith('__') && chat[k])) methods.push(k);
    }
    let helper = 0, parts = 0;
    for (const r of __chatRelatedRoots(chat)) {
        try { if (typeof r.__getParticipantNodeByLegacyCid === 'function') helper++; } catch (e) {}
        try { if (r.__participants && r.__participants.models) parts += r.__participants.models.length; } catch (e) {}
    }
    return (cached ? 'cached+' : 'found+') + ' methods=[' + methods.join(',') + '] helpers=' + helper +
        ' __participants~' + parts + ' ' + __listParticipantCids(chat, 6);
}
""";

    /// <summary>
    /// Runs in every document/frame before page scripts. Hooks IMVU ServiceProvider.register
    /// so we capture activeChat when the room creates it — no UI, no clicking.
    /// </summary>
    private const string ImvuActiveChatHookJs = """
(function () {
  function installOn(w) {
    if (!w) return;
    try {
      if (w.__imvuCompanionHooksInstalled) {
        // still try to read already-registered activeChat
        try {
          for (const k of Object.getOwnPropertyNames(w)) {
            try {
              const v = w[k];
              if (v && typeof v.get === 'function') {
                const ac = v.get('activeChat');
                if (ac) {
                  w.__imvuCompanionActiveChat = ac;
                  try { if (w.top) w.top.__imvuCompanionActiveChat = ac; } catch (e) {}
                }
              }
            } catch (e) {}
          }
        } catch (e) {}
        return;
      }
      w.__imvuCompanionHooksInstalled = true;
    } catch (e) { return; }

    function capture(name, value) {
      if (name !== 'activeChat' || !value) return;
      try { w.__imvuCompanionActiveChat = value; } catch (e) {}
      try { if (w.top) w.top.__imvuCompanionActiveChat = value; } catch (e) {}
      try { if (w.parent && w.parent !== w) w.parent.__imvuCompanionActiveChat = value; } catch (e) {}
    }

    function hookRegisterFn(obj) {
      if (!obj || obj.__imvuCompanionRegHooked) return;
      const orig = obj.register;
      if (typeof orig !== 'function') return;
      obj.register = function (name, value) {
        try { capture(name, value); } catch (e) {}
        return orig.apply(this, arguments);
      };
      obj.__imvuCompanionRegHooked = true;
    }

    function scanAndHook() {
      try {
        if (w.IMVU) {
          if (w.IMVU.ServiceProvider && w.IMVU.ServiceProvider.prototype)
            hookRegisterFn(w.IMVU.ServiceProvider.prototype);
          if (w.IMVU.serviceProvider) hookRegisterFn(w.IMVU.serviceProvider);
        }
      } catch (e) {}
      try {
        for (const k of Object.getOwnPropertyNames(w)) {
          try {
            const v = w[k];
            if (!v || typeof v !== 'object') continue;
            if (typeof v.register === 'function' && typeof v.get === 'function') {
              hookRegisterFn(v);
              try {
                const ac = v.get('activeChat');
                if (ac) capture('activeChat', ac);
              } catch (e) {}
            }
            if (v.prototype && typeof v.prototype.register === 'function')
              hookRegisterFn(v.prototype);
          } catch (e) {}
        }
      } catch (e) {}
    }

    scanAndHook();
    let n = 0;
    const t = w.setInterval(function () {
      scanAndHook();
      if (++n > 180) w.clearInterval(t);
    }, 500);
  }

  try {
    window.__imvuCompanionInstallHooks = installOn;
    installOn(window);
    // same-origin chat iframes
    try {
      for (const f of document.querySelectorAll('iframe')) {
        try { installOn(f.contentWindow); } catch (e) {}
      }
    } catch (e) {}
  } catch (e) {}
})();
""";

    private async Task<string?> SendToImvuChatViaWebView(string text, bool whisperReply = false, string? whisperRowRef = null,
        string? whisperSpeaker = null, string? whisperCmd = null, bool proactiveWhisperToUser = false,
        string? joinUserId = null, CancellationToken ct = default)
    {
        if (!IsWebViewReady) return null;
        ct.ThrowIfCancellationRequested();

        if (!whisperReply)
        {
            await EnsurePublicChatModeAsync();
            await RunPublicChatSendJsAsync(text);
            AppendActivityLog($"[Sent] {text}", LogCategory.Sent);
            return "ok";
        }

        if (proactiveWhisperToUser)
        {
            if (string.IsNullOrEmpty(joinUserId))
            {
                AppendLog("Proactive whisper skipped — need join uId for silent send", LogCategory.Warning);
                return "no-join-uid";
            }

            // Silent path via IMVU APIs. Result is polled from window.__imvuWhisperResult
            // (returning Promises from ExecuteScriptAsync often shows up as "{}" in the host).
            string escapedUid = JsonSerializer.Serialize(joinUserId);
            string escapedText = JsonSerializer.Serialize(text);
            string escapedName = JsonSerializer.Serialize(whisperSpeaker ?? "");
            string jsBase = FindChatRootJs + ProactiveWhisperJs;

            string? result = null;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                string? started = await RunJsStringAsync(jsBase + $$"""
return silentWhisperStart({{escapedUid}}, {{escapedText}}, {{escapedName}});
""", logErrors: true);
                if (started != "started")
                {
                    result = started ?? "start-failed";
                    break;
                }

                // Poll async work (participant lookup + send)
                for (int poll = 0; poll < 40; poll++)
                {
                    ct.ThrowIfCancellationRequested();
                    await Task.Delay(100, ct);
                    result = await RunJsStringAsync(jsBase + "return silentWhisperPoll();", logErrors: false);
                    if (result == null || result == "pending" || result == "{}")
                        continue;
                    break;
                }

                if (result == "ok" || (result != null && result.StartsWith("ok", StringComparison.Ordinal)))
                {
                    AppendActivityLog($"[Whisper] {whisperSpeaker ?? joinUserId} {text}", LogCategory.Whisper);
                    return "ok";
                }

                if (result != null && result.StartsWith("no-participant:", StringComparison.Ordinal))
                {
                    await Task.Delay(200, ct);
                    continue;
                }

                // Other errors — stop retrying
                break;
            }

            if (string.IsNullOrEmpty(result) || result == "pending" || result == "{}")
                result = result ?? "js-null";

            string? probe = await RunJsStringAsync(jsBase + "return silentWhisperProbe();", logErrors: false);
            AppendLog("Silent whisper failed: " + result + (string.IsNullOrEmpty(probe) ? "" : " | " + probe), LogCategory.Warning);
            return result;
        }

        string escapedRowRef = JsonSerializer.Serialize(whisperRowRef ?? "");
        string escapedSpeaker = JsonSerializer.Serialize(whisperSpeaker ?? "");
        string escapedCmd = JsonSerializer.Serialize(whisperCmd ?? "");

        string? clickResult = await RunJsStringAsync(FindChatRootJs + WhisperFindRowJs + $$"""
const rowRef = {{escapedRowRef}};
const targetSpeaker = {{escapedSpeaker}};
const targetCmd = {{escapedCmd}};
const cont = __imvuFindChatRoot().cont;
const row = findWhisperRow(cont, rowRef, targetSpeaker, targetCmd);
if (!row) return 'no-whisper-row';
const btn = row.querySelector('.icon-reply_from_whisper, [class*="icon-reply_from_whisper"], [class*="reply_from_whisper"]');
if (!btn) return 'no-reply-btn';
try { btn.scrollIntoView({ block: 'nearest' }); } catch(e) {}
const clickEvt = new MouseEvent('click', { bubbles: true, cancelable: true, view: window });
btn.dispatchEvent(clickEvt);
if (typeof btn.click === 'function') btn.click();
return 'clicked';
""", logErrors: true);

        if (clickResult != "clicked")
        {
            AppendLog("Whisper click: " + (clickResult ?? "js-null"), LogCategory.Warning);
            return clickResult ?? "js-null";
        }

        await Task.Delay(700);
        await RunPublicChatSendJsAsync(text);
        await FinishWhisperSendAsync(whisperSpeaker ?? "?", text);
        return "ok";
    }

    private async Task FinishWhisperSendAsync(string targetName, string message)
    {
        await Task.Delay(400);
        string? close1 = await ExitWhisperModeAsync();
        await Task.Delay(200);
        string? close2 = await ExitWhisperModeAsync();
        if (close1 != "closed" && close2 != "closed")
            AppendLog("Whisper panel may still be open (" + (close2 ?? close1 ?? "?") + ")", LogCategory.Warning);
        AppendActivityLog($"[Whisper] {targetName} {message}", LogCategory.Whisper);
    }

    private const string ChatObserverJs = FindChatRootJs + """
const post = (s) => { try { window.chrome.webview.postMessage(s); } catch(e) {} };
    const bad = /radio|on air|now playing|http|www\.|listen|click here|powered by|imvu\.com/i;
    const joinPhrases = /joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered|is\s+now\s+in\s+the\s+chat/i;
    const root = __imvuFindChatRoot();
    const cont = root.cont;
    window._seenJoinRows = new WeakSet();
    window._seenCmdKeys = new Set();
    function firstLine(t) { return (t || '').trim().split(/[\n\r]/)[0].trim(); }
    function norm(t) { return (t || '').replace(/\s+/g, ' ').trim(); }
    function hasVisibleName(name) {
        const s = (name || '').replace(/[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]/g, '').trim();
        return /[\p{L}\p{N}]/u.test(s);
    }
    function isJoinText(t) {
        t = norm(t);
        if (!t || t.length > 200 || t.length < 6 || bad.test(t) || t.includes('!')) return false;
        if (/left\s+the\s+chat/i.test(t)) return false;
        return joinPhrases.test(t);
    }
    const joinNameRx = /^(.+?)\s+(joined\s+the\s+chat|has\s+joined(?:\s+the\s+room)?|joined(?:\s+the\s+room)?|entered\s+the\s+room|has\s+entered(?:\s+the\s+room)?|is\s+now\s+in\s+the\s+chat)\s*\.?\s*$/i;
    function joinLinesFromRow(row) {
        if (!row) return [];
        return (row.innerText || row.textContent || '')
            .split(/[\n\r]+/)
            .map(l => norm(l))
            .filter(l => l.length >= 6 && l.length <= 100 && isJoinText(l));
    }
    function nameFromJoinLine(line) {
        const m = norm(line).match(joinNameRx);
        return m ? norm(m[1]) : '';
    }
    function parseJoinRow(row) {
        if (!row || row === cont) return null;
        const lines = joinLinesFromRow(row);
        if (!lines.length) return null;
        const text = lines[lines.length - 1];
        let name = nameFromJoinLine(text);
        if (!name) name = nameFromJoinAvatarImg(row);
        name = norm(name);
        if (!hasVisibleName(name) || isJoinText(name)) return null;
        return { name, text, row };
    }
    function nameFromJoinAvatarImg(row) {
        const wrapper = getJoinRowWrapper(row) || row;
        const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length < 1) return '';
        const firstDiv = kids[0];
        const img = firstDiv.querySelector('img');
        if (img) {
            const alt = norm(img.alt || img.getAttribute('title') || img.getAttribute('aria-label') || '');
            if (alt.length >= 1 && alt.length <= 60 && !isJoinText(alt) && !/^https?:/i.test(alt)) return alt;
        }
        const link = firstDiv.querySelector('a[title], [title], [aria-label], [data-username], [data-user]');
        if (link) {
            const t = norm(link.getAttribute('title') || link.getAttribute('aria-label') || link.getAttribute('data-username') || link.getAttribute('data-user') || '');
            if (t.length >= 1 && t.length <= 60 && !isJoinText(t)) return t;
        }
        const firstTxt = norm(firstDiv.innerText || firstDiv.textContent || '');
        if (firstTxt.length >= 1 && firstTxt.length <= 60 && !isJoinText(firstTxt)) return firstTxt;
        return '';
    }
    function extractUserIdFromNode(node) {
        if (!node || !node.getAttribute) return '';
        const dataId = node.getAttribute('data-id') || '';
        const m = dataId.match(/user\/user-(\d+)/i);
        return m ? m[1] : '';
    }
    function extractUserIdFromWrapper(wrapper) {
        if (!wrapper) return '';
        let node = wrapper;
        for (let d = 0; node && d < 12; d++) {
            const uid = extractUserIdFromNode(node);
            if (uid) return uid;
            node = node.parentElement;
        }
        return '';
    }
    function getJoinRowWrapper(row) {
        if (!row) return null;
        let node = row;
        let fallback = null;
        for (let d = 0; node && d < 12; d++) {
            const kids = Array.from(node.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
            if (kids.length >= 2) {
                const secondTxt = norm(kids[1].innerText || kids[1].textContent || '');
                if (isJoinText(secondTxt)) {
                    if (extractUserIdFromNode(node)) return node;
                    if (!fallback) fallback = node;
                }
            }
            node = node.parentElement;
        }
        return fallback || row;
    }
    function emitJoin(j) {
        if (!j || !j.row) return;
        let name = norm(j.name);
        if (!name) name = nameFromJoinLine(j.text);
        if (!name) name = norm(nameFromJoinAvatarImg(j.row));
        if (!hasVisibleName(name) || isJoinText(name)) return;
        const wrapper = getJoinRowWrapper(j.row) || j.row;
        const userId = extractUserIdFromWrapper(wrapper);
        if (window._seenJoinRows.has(wrapper)) return;
        window._seenJoinRows.add(wrapper);
        let joinRef = 'j' + Date.now() + '_' + Math.random().toString(36).slice(2, 7);
        try {
            wrapper.setAttribute('data-imvu-bot-join', joinRef);
            if (userId) wrapper.setAttribute('data-imvu-bot-user-id', userId);
        } catch(e) {}
        post(name + "\t" + j.text + "\t0\t" + joinRef + "\t" + (userId || ''));
    }
    function seedExistingJoins() {
        const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], [class*="join"], li, div');
        const start = Math.max(0, rows.length - 40);
        for (let i = rows.length - 1; i >= start; i--) {
            const j = parseJoinRow(rows[i]);
            if (!j) continue;
            const wrapper = getJoinRowWrapper(j.row) || j.row;
            window._seenJoinRows.add(wrapper);
        }
    }
    function scanRecentJoins() {
        if (window._joinPollPaused) return;
        const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], [class*="join"], li, div');
        const start = Math.max(0, rows.length - 15);
        for (let i = rows.length - 1; i >= start; i--) {
            const j = parseJoinRow(rows[i]);
            if (j) emitJoin(j);
        }
    }
    function findJoinInAddedNode(n) {
        if (!n) return null;
        const el = n.nodeType === 1 ? n : n.parentElement;
        if (!el) return null;
        const candidates = [];
        if (el.closest) {
            const row = el.closest('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], li');
            if (row && row !== cont) candidates.push(row);
        }
        candidates.push(el);
        if (el.querySelectorAll) {
            for (const sub of el.querySelectorAll('[class*="msg"], [class*="system"], [class*="event"], div, li')) {
                if (sub !== cont) candidates.push(sub);
            }
        }
        for (const c of candidates) {
            const j = parseJoinRow(c);
            if (j) return j;
        }
        return null;
    }
    function getSpeakerFromItem(item) {
        if (!item) return '';
        const sels = ['.cs2-name', '[class*="cs2-name"]', '[class*="username"]', '[class*="display-name"]', '[class*="user-name"]', '[class*="user"]', '[data-user]', '[data-username]'];
        for (const sel of sels) {
            const userCand = item.querySelector(sel);
            if (!userCand) continue;
            let sp = firstLine(userCand.textContent || userCand.innerText || '');
            if (sp.length >= 1 && sp.length <= 60 && !bad.test(sp)) return sp;
        }
        const prev = item.previousElementSibling;
        if (prev) {
            for (const sel of sels) {
                const userCand = prev.querySelector(sel);
                if (!userCand) continue;
                let sp = firstLine(userCand.textContent || userCand.innerText || '');
                if (sp.length >= 1 && sp.length <= 60 && !bad.test(sp)) return sp;
            }
        }
        return '';
    }
    function getMessageWrapper(row) {
        if (!row) return null;
        return row.closest('[class*="msg"], [class*="message"], [class*="chat-line"], li') || row;
    }
    function isWhisperMessage(row) {
        let el = getMessageWrapper(row);
        for (let i = 0; i < 8 && el; i++) {
            const cls = (el.className || '').toString();
            if (/\bis-presenter\b/i.test(cls)) return false;
            if (/\bwhisper\b/i.test(cls) && !/reply_from_whisper|reply-to-whisper|whisper-reply|icon-reply/i.test(cls)) return true;
            el = el.parentElement;
        }
        return false;
    }
    function isValidSpeaker(sp) {
        if (!sp || sp.length < 1 || sp.length > 50) return false;
        if (sp.includes('!')) return false;
        if (/commands:/i.test(sp)) return false;
        if (/^\s|\s{2,}/.test(sp.replace(/\s+to\s+me$/i, ''))) return false;
        return true;
    }
    function getCommandTextFromRow(row) {
        const wrapper = getMessageWrapper(row) || row;
        const raw = (wrapper.innerText || wrapper.textContent || '');
        const lines = raw.split(/[\n\r]+/).map(l => l.trim()).filter(l => l.length > 0);
        for (const line of lines) {
            const t = norm(line);
            if (/^!\S+/.test(t) && t.length >= 2 && t.length <= 300 && !bad.test(t)) return t;
        }
        return '';
    }
    function emitCommandFromRow(row, batchRows) {
        if (!row || row === cont) return;
        const wrapper = getMessageWrapper(row) || row;
        if (batchRows && batchRows.has(wrapper)) return;
        if (batchRows) batchRows.add(wrapper);
        const cmdText = getCommandTextFromRow(wrapper);
        if (!cmdText) return;
        const speaker = getSpeakerFromItem(wrapper);
        if (!isValidSpeaker(speaker)) return;
        const whisper = isWhisperMessage(wrapper);
        let rowRef = '';
        if (whisper) {
            rowRef = 'w' + Date.now() + '_' + Math.random().toString(36).slice(2, 7);
            try { wrapper.setAttribute('data-imvu-bot-cmd', rowRef); } catch(e) {}
        }
        const dedupe = (speaker || '') + '\t' + cmdText.toLowerCase();
        if (window._seenCmdKeys.has(dedupe)) return;
        window._seenCmdKeys.add(dedupe);
        post(speaker + "\t" + cmdText + "\t" + (whisper ? '1' : '0') + "\t" + rowRef);
    }
    function scanRecentCommands() {
        const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="whisper"], li');
        const start = Math.max(0, rows.length - 25);
        for (let i = rows.length - 1; i >= start; i--) emitCommandFromRow(rows[i], null);
    }
    if (window._o) { try { window._o.disconnect(); } catch(e){} }
    if (window._joinPoll) { clearInterval(window._joinPoll); window._joinPoll = null; }
    if (window._cmdPoll) { clearInterval(window._cmdPoll); window._cmdPoll = null; }
    window._o = new MutationObserver((ms) => {
        const batchRows = new Set();
        for (let m of ms) {
            for (let n of m.addedNodes) {
                if (n.nodeType !== 1 && n.nodeType !== 3) continue;
                const join = findJoinInAddedNode(n);
                if (join) { emitJoin(join); continue; }
                let el = n.nodeType === 3 ? n.parentElement : n;
                if (!el) continue;
                const row = el.closest ? el.closest('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="whisper"], [class*="system"], li') : el;
                if (!row || row === cont) continue;
                emitCommandFromRow(row, batchRows);
            }
        }
    });
    window._o.observe(cont, { childList: true, subtree: true, characterData: true });
    seedExistingJoins();
    window._joinPoll = setInterval(scanRecentJoins, 2000);
    window._cmdPoll = setInterval(scanRecentCommands, 2000);
window._lastChatContainer = (root.hasStream ? 'chat-stream2' : 'body-fallback')
    + (root.hasInput ? '+input' : '') + ' | ' + (cont.className || cont.tagName);
""";

    private const string CollectExistingJoinUidsJs = FindChatRootJs + """
const bad = /radio|on air|now playing|http|www\.|listen|click here|powered by|imvu\.com/i;
const joinPhrases = /joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered|is\s+now\s+in\s+the\s+chat/i;
const cont = __imvuFindChatRoot().cont;
function norm(t) { return (t || '').replace(/\s+/g, ' ').trim(); }
function isJoinText(t) {
    t = norm(t);
    if (!t || t.length > 200 || t.length < 6 || bad.test(t) || t.includes('!')) return false;
    if (/left\s+the\s+chat/i.test(t)) return false;
    return joinPhrases.test(t);
}
function extractUserIdFromNode(node) {
    if (!node || !node.getAttribute) return '';
    const dataId = node.getAttribute('data-id') || '';
    const m = dataId.match(/user\/user-(\d+)/i);
    return m ? m[1] : '';
}
function extractUserIdFromWrapper(wrapper) {
    if (!wrapper) return '';
    let node = wrapper;
    for (let d = 0; node && d < 12; d++) {
        const uid = extractUserIdFromNode(node);
        if (uid) return uid;
        node = node.parentElement;
    }
    return '';
}
function getJoinRowWrapper(row) {
    if (!row) return null;
    let node = row;
    let fallback = null;
    for (let d = 0; node && d < 12; d++) {
        const kids = Array.from(node.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
        if (kids.length >= 2) {
            const secondTxt = norm(kids[1].innerText || kids[1].textContent || '');
            if (isJoinText(secondTxt)) {
                if (extractUserIdFromNode(node)) return node;
                if (!fallback) fallback = node;
            }
        }
        node = node.parentElement;
    }
    return fallback || row;
}
function joinLinesFromRow(row) {
    if (!row) return [];
    return (row.innerText || row.textContent || '')
        .split(/[\n\r]+/)
        .map(l => norm(l))
        .filter(l => l.length >= 6 && l.length <= 100 && isJoinText(l));
}
function parseJoinRow(row) {
    if (!row || row === cont) return null;
    const lines = joinLinesFromRow(row);
    if (!lines.length) return null;
    return { row };
}
const uids = new Set();
const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], [class*="join"], li, div');
const start = Math.max(0, rows.length - 40);
for (let i = rows.length - 1; i >= start; i--) {
    const j = parseJoinRow(rows[i]);
    if (!j) continue;
    const wrapper = getJoinRowWrapper(j.row) || j.row;
    const userId = extractUserIdFromWrapper(wrapper);
    if (userId) uids.add(userId);
}
return Array.from(uids).join(',');
""";

    private async Task SeedGreetedFromExistingChatAsync()
    {
        if (!IsWebViewReady) return;
        try
        {
            string? raw = await RunJsStringAsync(CollectExistingJoinUidsJs, logErrors: false);
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
        var diag = await RunJsStringAsync(FindChatRootJs + """
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
        await RunJsVoidAsync(ImvuActiveChatHookJs + """
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

        bool ok = await RunJsVoidAsync(ChatObserverJs, logErrors: true);
        var hint = await RunJsStringAsync("return window._lastChatContainer || '';", logErrors: true);
        AppendLog(ok ? "Observer installed — " + (string.IsNullOrEmpty(hint) ? "(no container hint)" : hint)
                      : "Observer FAILED to install — check JS error above", LogCategory.Info);

        var probe = await RunJsStringAsync(FindChatRootJs + ProactiveWhisperJs + "return silentWhisperProbe();", logErrors: false);
        if (!string.IsNullOrEmpty(probe))
            AppendLog("Whisper API: " + probe, LogCategory.Info);

        await RunChatDiagnosticsAsync();
    }
}