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
            if (_botRunning && IsWebViewReady)
            {
                _observerBoundUrl = null;
                await SetupChatObserver();
            }
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
                if (!_botRunning) return;
                if (IsJoinLine(txt) || txt.Contains('!'))
                {
                    string wTag = isWhisper ? " [whisper]" : "";
                    string uidTag = !string.IsNullOrEmpty(joinUserId) ? $" uid={joinUserId}" : "";
                    AppendLog($"Observed{wTag}{uidTag}: {(string.IsNullOrEmpty(sp) ? "?" : sp)} | {txt}", LogCategory.Info);
                }
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

        bool inChat = url.Contains("/chat", StringComparison.OrdinalIgnoreCase) ||
                      url.Contains("room", StringComparison.OrdinalIgnoreCase);
        string state = _botRunning ? "Bot RUNNING" : "Ready";
        UpdateStatusText(inChat ? $"{state} | Chat page detected" : $"{state} | Open a chat room on the left");
    }

    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        if (IsWebViewReady) ImvuWebView.CoreWebView2.Navigate(ImvuHomeUrl);
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
            }
            catch { }
            return null;
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
            return ParseJsStringResult(json);
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
        bool urlLooksLikeChat = url.Contains("/chat", StringComparison.OrdinalIgnoreCase) ||
                                url.Contains("room", StringComparison.OrdinalIgnoreCase);
        if (!urlLooksLikeChat)
        {
            var detected = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
return (r.hasStream || r.hasInput) ? 'yes' : 'no';
""");
            if (detected != "yes")
            {
                AppendLog("Open your chat room in the left panel, then Start Bot.", LogCategory.Warning);
                return false;
            }
            AppendLog("Chat UI detected (overlay — URL may not show /chat).", LogCategory.Info);
        }
        return true;
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
    const kids = Array.from(wrapper.children).filter(c => (c.tagName || '').toLowerCase() === 'div');
    if (kids.length < 1) return null;
    const firstDiv = kids[0];
    return firstDiv.querySelector('button, [role="button"]')
        || firstDiv.querySelector('a button, a [role="button"]')
        || firstDiv.querySelector('button img, [role="button"] img')
        || firstDiv.querySelector('a img, a [class*="avatar"], a')
        || firstDiv.querySelector('img[class*="avatar"], img')
        || firstDiv.querySelector('[class*="avatar"] img, [class*="avatar"]')
        || firstDiv;
}
function clickJoinAvatarForWhisper(joinRef, userId, expectedName, botName) {
    const wrapper = resolveJoinWrapper(joinRef, userId);
    if (!wrapper) return 'no-join-row';
    const wrapperUserId = extractUserIdFromWrapper(wrapper);
    if (userId && wrapperUserId && userId !== wrapperUserId) return 'wrong-user-id:' + wrapperUserId;
    const joinerName = joinNameFromWrapper(wrapper);
    const want = foldImvuName(expectedName);
    const bot = foldImvuName(botName || '');
    const joiner = foldImvuName(joinerName);
    if (bot && want && want === bot) return 'joiner-is-bot';
    if (bot && joiner && joiner === bot) return 'joiner-is-bot';
    const uidTrusted = userId && wrapperUserId && userId === wrapperUserId;
    if (!uidTrusted && joiner && want && !namesRoughlyMatch(joiner, want)) return 'wrong-join-row:' + (joinerName || '?');
    const clickTarget = joinAvatarClickTarget(wrapper);
    if (!clickTarget) return 'no-avatar-button';
    try { window._joinWhisperUserId = wrapperUserId || userId || ''; } catch (e) {}
    robustClick(clickTarget);
    return 'avatar-clicked' + (wrapperUserId ? ':uid=' + wrapperUserId : '');
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
    for (const doc of __imvuAllDocs()) {
        roots.push(doc);
        try {
            doc.querySelectorAll('*').forEach(el => { if (el.shadowRoot) roots.push(el.shadowRoot); });
        } catch (e) {}
    }
    return roots;
}
function findVisibleMenus() {
    const menus = [];
    const sels = '[role="menu"], [class*="context-menu"], [class*="dropdown-menu"], [class*="popup-menu"], [class*="user-menu"], [class*="profile-menu"], [class*="action-menu"], ul[class*="menu"]';
    for (const root of allSearchRoots()) {
        for (const el of root.querySelectorAll(sels)) {
            if (isVisibleEl(el)) menus.push(el);
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
function whisperItemInMenu(menu) {
    if (!menu) return null;
    for (const item of menu.querySelectorAll('[data-menu-item="send_a_whisper"]')) {
        if (isVisibleEl(item)) return item;
    }
    for (const el of menu.querySelectorAll('li, [role="menuitem"], button, a, div, span')) {
        const dm = el.getAttribute && el.getAttribute('data-menu-item');
        if (dm === 'send_a_whisper' && isVisibleEl(el)) return el;
        const txt = (el.textContent || '').replace(/\s+/g, ' ').trim().toLowerCase();
        if ((/^send a whisper$/i.test(txt) || txt === 'whisper') && txt.length < 30 && isVisibleEl(el)) return el;
    }
    return null;
}
function findSendAWhisperMenuItem(userId, joinRef) {
    const wrapper = resolveJoinWrapper(joinRef, userId);
    const clickRect = wrapper && isVisibleEl(wrapper) ? wrapper.getBoundingClientRect() : null;
    const menus = findVisibleMenus();
    if (userId) {
        for (const menu of menus) {
            if (!menuMatchesUserId(menu, userId)) continue;
            const item = whisperItemInMenu(menu);
            if (item) return item;
        }
    }
    if (clickRect) {
        let best = null, bestDist = 1e12;
        for (const menu of menus) {
            const item = whisperItemInMenu(menu);
            if (!item) continue;
            const mr = menu.getBoundingClientRect();
            const dx = (mr.left + mr.width / 2) - (clickRect.left + clickRect.width / 2);
            const dy = (mr.top + mr.height / 2) - (clickRect.top + clickRect.height / 2);
            const dist = dx * dx + dy * dy;
            if (dist < bestDist) { bestDist = dist; best = item; }
        }
        if (best) return best;
    }
    for (const menu of menus) {
        const item = whisperItemInMenu(menu);
        if (item) return item;
    }
    for (const root of allSearchRoots()) {
        for (const item of root.querySelectorAll('[data-menu-item="send_a_whisper"]')) {
            if (isVisibleEl(item)) return item;
        }
    }
    return null;
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
function clickSendAWhisperMenu(userId, joinRef) {
    const item = findSendAWhisperMenuItem(userId, joinRef);
    if (!item) return 'no-menu-item';
    robustClick(item);
    return 'menu-clicked';
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
            AppendLog("Sent: " + text, LogCategory.Sent);
            return "ok";
        }

        if (proactiveWhisperToUser)
        {
            if (string.IsNullOrEmpty(whisperRowRef) && string.IsNullOrEmpty(joinUserId))
            {
                AppendLog("Proactive whisper skipped — no join row ref or user id", LogCategory.Warning);
                return "no-join-ref";
            }
            await SetJoinPollPausedAsync(true);
            try
            {
                string? openResult = await OpenProactiveWhisperToUserAsync(whisperSpeaker, whisperRowRef, joinUserId, ct);
                if (openResult != "ok")
                {
                    AppendLog("Proactive whisper open: " + (openResult ?? "js-null"), LogCategory.Warning);
                    await ForceDismissWhisperUiAsync();
                    return openResult ?? "js-null";
                }

                string escapedTarget = JsonSerializer.Serialize(whisperSpeaker);
                string escapedBotName = JsonSerializer.Serialize(_botDisplayName ?? "");
                string? preSend = await RunJsStringAsync(FindChatRootJs + ProactiveWhisperJs + $$"""
const expected = {{escapedTarget}};
const botName = {{escapedBotName}};
const trustUid = {{JsonSerializer.Serialize(joinUserId ?? "")}};
const v = proactiveWhisperReady(expected, botName, true, trustUid);
if (v === 'ok') return 'ok:' + (whisperTargetDebug() || 'join-menu');
return v + ':' + whisperTargetDebug();
""", logErrors: true);
                ct.ThrowIfCancellationRequested();

                if (preSend == null || preSend.StartsWith("target-is-bot", StringComparison.Ordinal))
                {
                    AppendLog("Proactive whisper blocked: compose shows your account (styled @name = self)", LogCategory.Warning);
                    await ForceDismissWhisperUiAsync();
                    return "target-is-bot";
                }
                if (!preSend.StartsWith("ok:", StringComparison.Ordinal))
                {
                    AppendLog("Proactive whisper blocked: " + preSend, LogCategory.Warning);
                    await ForceDismissWhisperUiAsync();
                    return preSend;
                }
                string targetLabel = preSend["ok:".Length..];
                AppendLog("Whisper compose target: " + (string.IsNullOrWhiteSpace(targetLabel) || targetLabel == "(unreadable)" ? (whisperSpeaker ?? "joiner") + " (join menu)" : targetLabel), LogCategory.Info);

                await Task.Delay(500, ct);
                await RunPublicChatSendJsAsync(text);
                await FinishWhisperSendAsync("Whisper (join extra) → " + whisperSpeaker + ": " + text);
                return "ok";
            }
            catch (OperationCanceledException)
            {
                await ForceDismissWhisperUiAsync();
                throw;
            }
            finally
            {
                await SetJoinPollPausedAsync(false);
            }
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
        await FinishWhisperSendAsync("Whisper: " + text, LogCategory.Command);
        return "ok";
    }

    private async Task<string?> PollProactiveWhisperMenuAsync(string jsBase, string readyAfterMenuJs, string? joinUserId, string? joinRowRef, CancellationToken ct)
    {
        string escapedUserId = JsonSerializer.Serialize(joinUserId ?? "");
        string escapedJoinRef = JsonSerializer.Serialize(joinRowRef ?? "");
        string menuProbeJs = $$"""
return findSendAWhisperMenuItem({{escapedUserId}}, {{escapedJoinRef}}) ? 'menu-visible' : 'no-menu';
""";
        string menuClickJs = $$"""
return clickSendAWhisperMenu({{escapedUserId}}, {{escapedJoinRef}});
""";

        bool menuClicked = false;
        for (int i = 0; i < 20; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsBotActive) return "bot-stopped";
            await Task.Delay(menuClicked ? 200 : 250, ct);

            if (menuClicked)
            {
                string? ready = await RunJsStringAsync(jsBase + readyAfterMenuJs, logErrors: true);
                if (ready == "ok") return "ok";
                if (ready is "target-is-bot" or "no-joiner-name")
                    return ready;
                if (ready != null && ready.StartsWith("target-mismatch", StringComparison.Ordinal))
                    return ready;
                continue;
            }

            string? menu = await RunJsStringAsync(jsBase + menuProbeJs, logErrors: true);
            if (menu == "menu-visible")
            {
                string? clickMenu = await RunJsStringAsync(jsBase + menuClickJs, logErrors: true);
                if (clickMenu != "menu-clicked") return clickMenu ?? "menu-click-failed";
                menuClicked = true;
                await Task.Delay(600, ct);
            }
        }

        if (menuClicked)
        {
            string? finalReady = await RunJsStringAsync(jsBase + readyAfterMenuJs, logErrors: true);
            if (finalReady == "ok") return "ok";
            if (finalReady is "target-is-bot" or "no-joiner-name")
                return finalReady;
            return "menu-clicked-but-unverified:" + (finalReady ?? "?");
        }

        return "no-menu-item";
    }

    private async Task<string?> OpenProactiveWhisperToUserAsync(string targetUser, string? joinRowRef = null, string? joinUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(joinRowRef) && string.IsNullOrEmpty(joinUserId))
            return "no-join-ref";
        if (!IsBotActive)
            return "bot-stopped";

        string escapedTarget = JsonSerializer.Serialize(targetUser ?? "");
        string escapedJoinRef = JsonSerializer.Serialize(joinRowRef ?? "");
        string escapedUserId = JsonSerializer.Serialize(joinUserId ?? "");
        string escapedBotName = JsonSerializer.Serialize(_botDisplayName ?? "");
        string jsBase = FindChatRootJs + ProactiveWhisperJs;
        string readyAfterMenuJs = $$"""
const expected = {{escapedTarget}};
const botName = {{escapedBotName}};
return proactiveWhisperReady(expected, botName, true, {{escapedUserId}});
""";

        ct.ThrowIfCancellationRequested();
        await ForceDismissWhisperUiAsync();
        await Task.Delay(200, ct);

        string? clickResult = await RunJsStringAsync(jsBase + $$"""
return clickJoinAvatarForWhisper({{escapedJoinRef}}, {{escapedUserId}}, {{escapedTarget}}, {{escapedBotName}});
""", logErrors: true);

        if (clickResult == null || !clickResult.StartsWith("avatar-clicked", StringComparison.Ordinal))
        {
            AppendLog("Whisper click [join-avatar]: " + (clickResult ?? "js-null"), LogCategory.Warning);
            return clickResult ?? "click-failed";
        }

        AppendLog("Whisper click [join-avatar] → user menu " + clickResult, LogCategory.Info);
        await Task.Delay(450, ct);
        string? pollResult = await PollProactiveWhisperMenuAsync(jsBase, readyAfterMenuJs, joinUserId, joinRowRef, ct);
        if (pollResult == "ok")
        {
            AppendLog("Whisper opened via join-avatar (uid=" + (joinUserId ?? "?") + ")", LogCategory.Info);
            return "ok";
        }

        if (pollResult is "target-is-bot")
            AppendLog("Whisper [join-avatar] opened self — wrong target", LogCategory.Warning);
        else
            AppendLog("Whisper [join-avatar]: " + pollResult, LogCategory.Warning);

        await ForceDismissWhisperUiAsync();
        return pollResult ?? "no-menu-item";
    }

    private async Task FinishWhisperSendAsync(string logLine, LogCategory category = LogCategory.Sent)
    {
        await Task.Delay(400);
        string? close1 = await ExitWhisperModeAsync();
        await Task.Delay(200);
        string? close2 = await ExitWhisperModeAsync();
        if (close1 != "closed" && close2 != "closed")
            AppendLog("Whisper panel may still be open (" + (close2 ?? close1 ?? "?") + ")", LogCategory.Warning);
        AppendLog(logLine, category);
    }

    private const string ChatObserverJs = FindChatRootJs + """
const post = (s) => { try { window.chrome.webview.postMessage(s); } catch(e) {} };
    const bad = /radio|on air|now playing|http|www\.|listen|click here|powered by|imvu\.com/i;
    const joinPhrases = /joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered|is\s+now\s+in\s+the\s+chat/i;
    const root = __imvuFindChatRoot();
    const cont = root.cont;
    window._seenJoinKeys = new Set();
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
    function getJoinNameFromRow(row) {
        if (!row) return '';
        const sels = ['.cs2-name', '[class*="cs2-name"]', '[class*="username"]', '[class*="display-name"]', '[class*="user-name"]'];
        for (const sel of sels) {
            const nameEl = row.querySelector(sel);
            if (!nameEl) continue;
            let sp = norm(nameEl.textContent || nameEl.innerText || '');
            if (sp.length >= 1 && sp.length <= 60 && !bad.test(sp) && !isJoinText(sp)) return sp;
        }
        const prev = row.previousElementSibling;
        if (prev) {
            for (const sel of sels) {
                const nameEl = prev.querySelector(sel);
                if (!nameEl) continue;
                let sp = norm(nameEl.textContent || nameEl.innerText || '');
                if (sp.length >= 1 && sp.length <= 60 && !bad.test(sp) && !isJoinText(sp)) return sp;
            }
        }
        return '';
    }
    function parseJoinRow(row) {
        if (!row || row === cont) return null;
        const full = norm(row.innerText || row.textContent || '');
        if (!isJoinText(full) || full.length > 100) return null;
        const lineCount = (row.innerText || '').split(/[\n\r]+/).map(l => l.trim()).filter(Boolean).length;
        if (lineCount > 4) return null;
        let name = getJoinNameFromRow(row);
        if (!name) {
            const m = full.match(/^(.+?)\s+(joined\s+the\s+chat|has\s+joined(?:\s+the\s+room)?|joined(?:\s+the\s+room)?|entered\s+the\s+room|has\s+entered(?:\s+the\s+room)?|is\s+now\s+in\s+the\s+chat)\s*\.?\s*$/i);
            if (m) name = m[1].trim();
        }
        if (!name) name = nameFromJoinAvatarImg(row);
        name = norm(name);
        if (!hasVisibleName(name) || isJoinText(name)) return null;
        return { name, text: full, row };
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
        if (!name) name = norm(nameFromJoinAvatarImg(j.row));
        if (!name) name = norm(getJoinNameFromRow(j.row));
        if (!hasVisibleName(name)) return;
        const wrapper = getJoinRowWrapper(j.row) || j.row;
        const userId = extractUserIdFromWrapper(wrapper);
        const key = userId ? ('uid:' + userId) : name.toLowerCase();
        if (window._seenJoinKeys.has(key)) return;
        window._seenJoinKeys.add(key);
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
            const userId = extractUserIdFromWrapper(wrapper);
            window._seenJoinKeys.add(userId ? ('uid:' + userId) : j.name.toLowerCase());
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
        bool ok = await RunJsVoidAsync(ChatObserverJs, logErrors: true);
        var hint = await RunJsStringAsync("return window._lastChatContainer || '';", logErrors: true);
        AppendLog(ok ? "Observer installed — " + (string.IsNullOrEmpty(hint) ? "(no container hint)" : hint)
                      : "Observer FAILED to install — check JS error above", LogCategory.Info);
        await RunChatDiagnosticsAsync();
    }
}