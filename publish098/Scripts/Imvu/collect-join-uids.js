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
