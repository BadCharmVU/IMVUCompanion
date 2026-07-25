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
