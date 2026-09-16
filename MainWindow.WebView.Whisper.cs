using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace IMVUCompanion;

public partial class MainWindow
{
    private sealed class RoomSnapshot
    {
        public bool RestoredOpen { get; set; }
        public string RestoredId { get; set; } = "";
        public string LastRoomId { get; set; } = "";
        public string RoomName { get; set; } = "";
        public int MiniCount { get; set; }
        public List<string> Minimized { get; } = new();
        public List<string> Ids { get; } = new();
        public int Count => Ids.Count;

        public bool Contains(string? id) =>
            !string.IsNullOrEmpty(id) &&
            Ids.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

        public bool IsMinimized(string? id) =>
            !string.IsNullOrEmpty(id) &&
            Minimized.Any(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));

        public string PrimaryId
        {
            get
            {
                if (IsRealRoomId(RestoredId) && MiniCount == 0)
                    return RestoredId;
                if (Minimized.Count == 1) return Minimized[0];
                if (Ids.Count == 1) return Ids[0];
                if (IsRealRoomId(RestoredId)) return RestoredId;
                return IsRealRoomId(LastRoomId) ? LastRoomId : "";
            }
        }

        public bool Multiple
        {
            get
            {
                int n = MiniCount;
                if (RestoredOpen) n++;
                if (n > 1) return true;
                return Minimized.Count > 1 || Ids.Count > 1;
            }
        }
    }

    private async Task<RoomSnapshot> GetRoomSnapshotAsync()
    {
        var snap = new RoomSnapshot();
        if (!IsWebViewReady) return snap;
        string url = ImvuWebView.CoreWebView2.Source ?? "";
        if (!url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase))
            return snap;

        string? raw = await RunJsStringAsync(ImvuScripts.FindChatRoot + """
try { return JSON.stringify(__imvuCollectRoomSnapshot()); } catch (e) { return ''; }
""", logErrors: false);
        if (string.IsNullOrWhiteSpace(raw)) return snap;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            snap.RestoredOpen = root.TryGetProperty("restoredOpen", out var ro) &&
                                (ro.ValueKind == JsonValueKind.True ||
                                 (ro.ValueKind == JsonValueKind.String && ro.GetString() == "true"));
            if (root.TryGetProperty("restoredId", out var rid) && rid.ValueKind == JsonValueKind.String)
                snap.RestoredId = (rid.GetString() ?? "").Trim();
            if (root.TryGetProperty("lastRoomId", out var lid) && lid.ValueKind == JsonValueKind.String)
                snap.LastRoomId = (lid.GetString() ?? "").Trim();
            if (root.TryGetProperty("miniCount", out var mc) && mc.ValueKind == JsonValueKind.Number)
                snap.MiniCount = mc.GetInt32();
            if (root.TryGetProperty("roomName", out var rn) && rn.ValueKind == JsonValueKind.String)
                snap.RoomName = (rn.GetString() ?? "").Trim();
            void readList(string name, List<string> dest)
            {
                if (!root.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
                foreach (var el in arr.EnumerateArray())
                {
                    string s = (el.GetString() ?? "").Trim();
                    if (s.Length > 0 && !dest.Contains(s, StringComparer.OrdinalIgnoreCase))
                        dest.Add(s);
                }
            }
            readList("minimized", snap.Minimized);
            readList("ids", snap.Ids);
        }
        catch { }
        return snap;
    }

    private async Task<bool> IsActiveRoomPresentAsync()
    {
        if (!IsWebViewReady) return false;
        string url = ImvuWebView.CoreWebView2.Source ?? "";
        if (!url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var detected = await RunJsStringAsync(ImvuScripts.FindChatRoot + """
const r = (typeof __imvuFindRestoredChat === 'function') ? __imvuFindRestoredChat() : __imvuFindChatRoot();
return (r.hasStream && r.hasInput) ? 'yes' : 'no';
""", logErrors: false);
        return detected == "yes";
    }

    private static bool IsRealRoomId(string? id) =>
        !string.IsNullOrWhiteSpace(id) &&
        id.StartsWith("room-", StringComparison.OrdinalIgnoreCase);

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
            await RunJsStringAsync(ImvuScripts.FindChatRoot + ImvuScripts.ProactiveWhisper + """
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


    private async Task<string?> ExitWhisperModeAsync()
    {
        string js = ImvuScripts.ExitWhisperMode.Replace("{{CHAT_INPUT_SEL}}", ChatInputSelector);
        return await RunJsStringAsync(ImvuScripts.FindChatRoot + js, logErrors: true);
    }

    private async Task EnsurePublicChatModeAsync()
    {
        await RunJsStringAsync(ImvuScripts.FindChatRoot + ImvuScripts.ProactiveWhisper + "return dismissOpenUi();", logErrors: false);
        for (int i = 0; i < 4; i++)
        {
            string? r = await ExitWhisperModeAsync();
            if (r == "closed") return;
            await RunJsStringAsync(ImvuScripts.FindChatRoot + ImvuScripts.ProactiveWhisper + "return dismissOpenUi();", logErrors: false);
            await Task.Delay(150);
        }
    }

    private async Task ForceDismissWhisperUiAsync() => await EnsurePublicChatModeAsync();

    private async Task<string?> SendNativeDmAsync(string? userId, string text)
    {
        string uid = (userId ?? "").Trim();
        if (string.IsNullOrEmpty(uid))
            return "no-uid";
        if (!IsWebViewReady)
            return "webview-not-ready";
        string escapedUid = JsonSerializer.Serialize(uid);
        string escapedText = JsonSerializer.Serialize(text ?? "");
        string escapedSelf = JsonSerializer.Serialize(_selfDetectedUid ?? "");
        await RunJsVoidAsync(ImvuScripts.OpenNativeDm + $$"""
window.__imvuDmSendResult = 'pending';
Promise.resolve(__imvuSendNativeDm({{escapedUid}}, {{escapedText}}, {{escapedSelf}}))
  .then(function (r) { window.__imvuDmSendResult = String(r); })
  .catch(function (e) { window.__imvuDmSendResult = 'js-err:' + (e && e.message ? e.message : String(e)); });
""", logErrors: true);
        for (int i = 0; i < 40; i++)
        {
            try { await Task.Delay(250); } catch { break; }
            string? r = await RunJsStringAsync("return window.__imvuDmSendResult;", logErrors: false);
            if (string.IsNullOrWhiteSpace(r) || r == "{}" || r == "pending")
                continue;
            return r;
        }
        return "timeout";
    }

    /// <summary>Quick Escape/dismiss for proactive whisper retries (avoids multi-second close loops).</summary>
    private async Task QuickDismissWhisperUiAsync()
    {
        await RunJsStringAsync(ImvuScripts.FindChatRoot + ImvuScripts.ProactiveWhisper + "return dismissOpenUi();", logErrors: false);
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
        await RunJsVoidAsync(ImvuScripts.FindChatRoot + $$"""
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



    /// <summary>
    /// Runs in every document/frame before page scripts. Hooks IMVU ServiceProvider.register
    /// so we capture activeChat when the room creates it — no UI, no clicking.
    /// </summary>

    private async Task<string?> SendToImvuChatViaWebView(string text, bool whisperReply = false, string? whisperRowRef = null,
        string? whisperSpeaker = null, string? whisperCmd = null, bool proactiveWhisperToUser = false,
        string? joinUserId = null, bool logSend = true, CancellationToken ct = default)
    {
        if (!IsWebViewReady) return null;
        ct.ThrowIfCancellationRequested();

        if (!whisperReply)
        {
            await EnsurePublicChatModeAsync();
            await RunPublicChatSendJsAsync(text);
            if (logSend)
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
            string jsBase = ImvuScripts.FindChatRoot + ImvuScripts.ProactiveWhisper;

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
                    if (logSend)
                        LogOutgoingToUser(whisperSpeaker ?? joinUserId, text, LogCategory.Whisper);
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

        string? clickResult = await RunJsStringAsync(ImvuScripts.FindChatRoot + ImvuScripts.WhisperFindRow + $$"""
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
        await FinishWhisperSendAsync(whisperSpeaker ?? "?", text, logSend);
        return "ok";
    }

    private async Task FinishWhisperSendAsync(string targetName, string message, bool logSend = true)
    {
        await Task.Delay(400);
        string? close1 = await ExitWhisperModeAsync();
        await Task.Delay(200);
        string? close2 = await ExitWhisperModeAsync();
        if (close1 != "closed" && close2 != "closed")
            AppendLog("Whisper panel may still be open (" + (close2 ?? close1 ?? "?") + ")", LogCategory.Warning);
        if (logSend)
            LogOutgoingToUser(targetName, message, LogCategory.Whisper);
    }


}
