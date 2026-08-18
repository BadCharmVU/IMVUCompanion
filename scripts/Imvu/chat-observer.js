const post = (s) => { try { window.chrome.webview.postMessage(s); } catch(e) {} };
    const bad = /radio|on air|now playing|http|www\.|listen|click here|powered by|imvu\.com/i;
    const joinPhrases = /joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered|is\s+now\s+in\s+the\s+chat/i;
    const leavePhrases = /left\s+the\s+chat/i;
    const presentPhrases = /\bis\s+in\s+the\s+chat/i;
    const root = __imvuFindChatRoot();
    const cont = root.cont;
    window._seenJoinRows = new WeakSet();
    window._seenCmdKeys = new Set();
    function firstLine(t) { return (t || '').trim().split(/[\n\r]/)[0].trim(); }
    function norm(t) { return (t || '').replace(/\s+/g, ' ').trim(); }
    function hasVisibleName(name) {
        return !isLayoutWhitespaceOnly(name);
    }
    function isLayoutWhitespaceOnly(s) {
        return !(s || '').replace(/[\s\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\u200B-\u200D\uFEFF\u00AD\u2060\u180E]/g, '').length;
    }
    function keepEnteredName(name) {
        if (isLayoutWhitespaceOnly(name)) return '';
        return (name || '').replace(/^[\s\u00A0\u1680\u2000-\u200A\u202F\u205F\u3000\u200B-\u200D\uFEFF]+|[\s\u00A0\u1680\u2000-\u200A\u202F\u205F\u3000\u200B-\u200D\uFEFF]+$/g, '');
    }
    function isBlankNameBeforePhrase(raw, phraseRx) {
        const s = raw || '';
        const m = s.match(phraseRx);
        if (!m) return false;
        const before = s.slice(0, m.index);
        return isLayoutWhitespaceOnly(before.replace(/!/g, ''));
    }
    function isJoinText(t) {
        t = norm(t);
        if (!t || t.length > 200 || t.length < 6 || bad.test(t) || t.includes('!')) return false;
        if (leavePhrases.test(t)) return false;
        return joinPhrases.test(t);
    }
    function isLeaveText(t) {
        t = norm(t);
        if (!t || t.length > 200 || t.length < 6 || bad.test(t)) return false;
        return leavePhrases.test(t);
    }
    function isPresentText(t) {
        t = norm(t);
        if (!t || t.length > 200 || t.length < 6 || bad.test(t)) return false;
        if (/is\s+now\s+in\s+the\s+chat/i.test(t)) return false;
        return presentPhrases.test(t);
    }
    const joinNameRx = /^(.+?)\s+(joined\s+the\s+chat|has\s+joined(?:\s+the\s+room)?|joined(?:\s+the\s+room)?|entered\s+the\s+room|has\s+entered(?:\s+the\s+room)?|is\s+now\s+in\s+the\s+chat)\s*\.?\s*$/i;
    const leaveNameRx = /^!?\s*(.+?)\s+left\s+the\s+chat\s*\.?\s*$/i;
    const presentNameRx = /^!?\s*(.+?)\s+is\s+in\s+the\s+chat\s*\.?\s*$/i;
    function joinLinesFromRow(row) {
        if (!row) return [];
        return (row.innerText || row.textContent || '')
            .split(/[\n\r]+/)
            .map(l => norm(l))
            .filter(l => l.length >= 6 && l.length <= 100 && isJoinText(l));
    }
    function nameFromJoinLine(line) {
        const m = (line || '').match(joinNameRx) || norm(line).match(joinNameRx);
        return m ? keepEnteredName(m[1]) : '';
    }
    function parseJoinRow(row) {
        if (!row || row === cont) return null;
        const rawLines = (row.innerText || row.textContent || '').split(/[\n\r]+/);
        const lines = joinLinesFromRow(row);
        let text = lines.length ? lines[lines.length - 1] : '';
        if (!text) {
            for (const raw of rawLines) {
                if (isJoinText(norm(raw)) || isBlankNameBeforePhrase(raw, /joined\s+the\s+chat|is\s+now\s+in\s+the\s+chat/i)) {
                    text = norm(raw) || raw;
                    break;
                }
            }
        }
        if (!text) return null;
        let name = keepEnteredName(nameFromJoinLine(text));
        if (!name) name = keepEnteredName(nameFromJoinAvatarImg(row));
        if (isJoinText(name)) name = '';
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
        const attrs = [
            node.getAttribute('data-id'),
            node.getAttribute('data-userid'),
            node.getAttribute('data-user-id'),
            node.getAttribute('data-user')
        ];
        for (const dataId of attrs) {
            if (!dataId) continue;
            const m = String(dataId).match(/user\/user-(\d+)/i);
            if (m) return m[1];
        }
        return '';
    }
    function extractUserIdFromWrapper(wrapper) {
        return extractUserIdDeep(wrapper);
    }
    function extractUserIdDeep(start) {
        if (!start) return '';
        let node = start;
        for (let d = 0; node && d < 16; d++) {
            const uid = extractUserIdFromNode(node);
            if (uid) return uid;
            if (node.querySelector) {
                const hit = node.querySelector('[data-id*="user/user-"]');
                if (hit) {
                    const down = extractUserIdFromNode(hit);
                    if (down) return down;
                }
            }
            node = node.parentElement;
        }
        return '';
    }
    function predForKind(kind) {
        if (kind === 'leave') return isLeaveText;
        if (kind === 'present') return isPresentText;
        return isJoinText;
    }
    function getSystemRowWrapper(row, kind) {
        if (!row) return null;
        const pred = predForKind(kind);
        let node = row;
        let fallback = null;
        for (let d = 0; node && d < 16; d++) {
            const uid = extractUserIdFromNode(node);
            const kids = Array.from(node.children || []).filter(c => (c.tagName || '').toLowerCase() === 'div');
            let textMatch = false;
            for (const k of kids) {
                const t = norm(k.innerText || k.textContent || '');
                if (pred(t) || pred(t.split(/[\n\r]/)[0] || '')) { textMatch = true; break; }
            }
            if (!textMatch) {
                const own = norm((node.innerText || '').split(/[\n\r]/)[0] || '');
                if (pred(own)) textMatch = true;
            }
            if (uid && textMatch) return node;
            if (uid && !fallback) fallback = node;
            if (textMatch && !fallback) fallback = node;
            node = node.parentElement;
        }
        return fallback || row;
    }
    function getJoinRowWrapper(row) {
        return getSystemRowWrapper(row, 'join');
    }
    function emitJoin(j) {
        if (!j || !j.row) return;
        let name = keepEnteredName(j.name);
        if (!name) name = keepEnteredName(nameFromJoinLine(j.text));
        if (!name) name = keepEnteredName(nameFromJoinAvatarImg(j.row));
        if (isJoinText(name)) name = '';
        const wrapper = getJoinRowWrapper(j.row) || j.row;
        const userId = extractUserIdFromWrapper(wrapper);
        if (!name && !userId) return;
        if (isSelfIdentity(name, userId, wrapper)) return;
        if (window._seenJoinRows.has(wrapper)) return;
        if (rememberSeenUid('join', userId)) return;
        try { if (userId && window._seenLeaveUids) window._seenLeaveUids.delete(userId); } catch (e) {}
        try {
            if (name && window._seenLeaveNames)
                window._seenLeaveNames.delete(String(name).toLowerCase());
        } catch (e) {}
        window._seenJoinRows.add(wrapper);
        let joinRef = 'j' + Date.now() + '_' + Math.random().toString(36).slice(2, 7);
        try {
            wrapper.setAttribute('data-imvu-bot-join', joinRef);
            if (userId) wrapper.setAttribute('data-imvu-bot-user-id', userId);
        } catch(e) {}
        post(name + "\t" + j.text + "\t0\t" + joinRef + "\t" + (userId || ''));
    }
    function systemLinesFromRow(row, pred) {
        if (!row) return [];
        return (row.innerText || row.textContent || '')
            .split(/[\n\r]+/)
            .map(l => norm(l))
            .filter(l => l.length >= 6 && l.length <= 100 && pred(l));
    }
    function parseNamedSystemRow(row, pred, nameRx) {
        if (!row || row === cont) return null;
        const lines = systemLinesFromRow(row, pred);
        if (!lines.length) return null;
        const text = lines[lines.length - 1];
        const m = text.match(nameRx) || norm(text).match(nameRx);
        let name = keepEnteredName(m ? m[1] : '');
        if (!name) name = keepEnteredName(nameFromJoinAvatarImg(row));
        if (pred(name) || isJoinText(name)) name = '';
        return { name, text, row };
    }
    function parseLeaveRow(row) { return parseNamedSystemRow(row, isLeaveText, leaveNameRx); }
    function parsePresentRow(row) { return parseNamedSystemRow(row, isPresentText, presentNameRx); }
    function isMyUserNode(node) {
        let el = node;
        for (let d = 0; el && d < 16; d++) {
            const cls = (el.className || '').toString();
            if (/(^|\s)my-user(\s|$)/.test(cls)) {
                const uid = extractUserIdDeep(el);
                if (uid) window.__imvuSelfUid = String(uid);
                return true;
            }
            el = el.parentElement;
        }
        return false;
    }
    function captureMyUserUid() {
        const roots = [];
        try { if (cont) roots.push(cont); } catch (e) {}
        try { roots.push(document); } catch (e) {}
        try { if (window.top && window.top.document) roots.push(window.top.document); } catch (e) {}
        for (const root of roots) {
            let nodes;
            try { nodes = root.querySelectorAll('.my-user'); } catch (e) { continue; }
            for (const n of nodes) {
                const uid = extractUserIdDeep(n) || extractUserIdFromNode(n);
                if (uid) {
                    window.__imvuSelfUid = String(uid);
                    return uid;
                }
            }
        }
        return window.__imvuSelfUid || '';
    }
    function isSelfIdentity(name, uid, row) {
        if (row && isMyUserNode(row)) return true;
        const selfUid = window.__imvuSelfUid || '';
        return !!(uid && selfUid && String(uid) === String(selfUid));
    }
    function rememberSeenUid(kind, uid) {
        if (!uid) return false;
        const key = kind === 'leave' ? '_seenLeaveUids' : kind === 'present' ? '_seenPresentUids' : '_seenJoinUids';
        if (!window[key]) window[key] = new Set();
        if (window[key].has(uid)) return true;
        window[key].add(uid);
        return false;
    }
    function emitTyped(j, kind) {
        if (!j || !j.row) return;
        const wrapper = getSystemRowWrapper(j.row, kind) || j.row;
        const seenKey = kind === 'leave' ? '_seenLeaveRows' : '_seenPresentRows';
        if (!window[seenKey]) window[seenKey] = new WeakSet();
        const userId = extractUserIdDeep(wrapper) || extractUserIdDeep(j.row);
        let name = keepEnteredName(j.name);
        if (!name && !userId) return;
        let prevUid = '';
        try { prevUid = wrapper.getAttribute('data-imvu-bot-user-id') || ''; } catch(e) {}
        if (window[seenKey].has(wrapper) && (prevUid || !userId)) return;
        if (isSelfIdentity(name, userId, wrapper)) return;
        if (kind === 'leave') {
            if (rememberSeenUid('leave', userId)) return;
            if (!userId && name) {
                if (!window._seenLeaveNames) window._seenLeaveNames = new Set();
                const nk = String(name).toLowerCase();
                if (window._seenLeaveNames.has(nk)) return;
                window._seenLeaveNames.add(nk);
            }
            try { if (userId && window._seenJoinUids) window._seenJoinUids.delete(userId); } catch (e) {}
            try { if (userId && window._seenPresentUids) window._seenPresentUids.delete(userId); } catch (e) {}
        } else {
            if (rememberSeenUid(kind, userId)) return;
            try { if (userId && window._seenLeaveUids) window._seenLeaveUids.delete(userId); } catch (e) {}
            try {
                if (name && window._seenLeaveNames)
                    window._seenLeaveNames.delete(String(name).toLowerCase());
            } catch (e) {}
        }
        window[seenKey].add(wrapper);
        let rowRef = kind.charAt(0) + Date.now() + '_' + Math.random().toString(36).slice(2, 7);
        try {
            wrapper.setAttribute('data-imvu-bot-' + kind, rowRef);
            if (userId) wrapper.setAttribute('data-imvu-bot-user-id', userId);
        } catch(e) {}
        post(name + "\t" + j.text + "\t" + kind + "\t" + rowRef + "\t" + (userId || ''));
    }
    function emitLeave(j) { emitTyped(j, 'leave'); }
    function emitPresent(j) { emitTyped(j, 'present'); }
    function getPlainChatFromRow(row) {
        const wrapper = getMessageWrapper(row) || row;
        const raw = (wrapper.innerText || wrapper.textContent || '');
        const lines = raw.split(/[\n\r]+/).map(l => norm(l)).filter(Boolean);
        const speaker = getSpeakerFromItem(wrapper);
        for (const line of lines) {
            if (speaker && line === speaker) continue;
            if (isJoinText(line) || isLeaveText(line) || isPresentText(line)) continue;
            if (/^(whisper|whispers|private|to me)$/i.test(line)) continue;
            if (bad.test(line)) continue;
            if (line.length >= 1 && line.length <= 400) return line;
        }
        return '';
    }
    function emitChatFromRow(row, batchRows) {
        if (!row || row === cont) return;
        const wrapper = getMessageWrapper(row) || row;
        if (batchRows && batchRows.has(wrapper)) return;
        if (getCommandTextFromRow(wrapper)) return;
        const text = getPlainChatFromRow(wrapper);
        if (!text) return;
        const speaker = getSpeakerFromItem(wrapper);
        if (!isValidSpeaker(speaker)) return;
        const whisper = isWhisperMessage(wrapper);
        const dedupe = (speaker || '') + '\tchat\t' + text.toLowerCase();
        if (window._seenCmdKeys.has(dedupe)) return;
        window._seenCmdKeys.add(dedupe);
        if (batchRows) batchRows.add(wrapper);
        post(speaker + "\t" + text + "\tchat\t" + (whisper ? '1' : '0') + "\t");
    }
    function seedExistingJoins() {
        const rows = cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], [class*="join"], li, div');
        const start = Math.max(0, rows.length - 80);
        for (let i = rows.length - 1; i >= start; i--) {
            const j = parseJoinRow(rows[i]);
            if (!j) continue;
            const wrapper = getJoinRowWrapper(j.row) || j.row;
            window._seenJoinRows.add(wrapper);
        }
    }
    function collectPresenceFromDataIds() {
        const nodes = cont.querySelectorAll('[data-id*="user/user-"]');
        for (const n of nodes) {
            const raw = n.innerText || n.textContent || '';
            const lines = raw.split(/[\n\r]+/);
            for (const rawLine of lines) {
                const line = norm(rawLine);
                const blank = isBlankNameBeforePhrase(rawLine, /is\s+in\s+the\s+chat/i);
                if (!isPresentText(line) && !blank) continue;
                const rawMatch = rawLine.match(presentNameRx) || line.match(presentNameRx);
                let name = keepEnteredName(rawMatch ? rawMatch[1] : '');
                if (blank) name = '';
                emitPresent({ name, text: line || rawLine, row: n });
            }
        }
    }
    function seedExistingPresence() {
        collectPresenceFromDataIds();
        const rows = cont.querySelectorAll('[data-id*="user/user-"], [class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], [class*="join"], li');
        for (let i = 0; i < rows.length; i++) {
            const p = parsePresentRow(rows[i]);
            if (p) emitPresent(p);
        }
    }
    window.__imvuReseedPresence = function() {
        try { captureMyUserUid(); } catch (e) {}
        try { seedExistingPresence(); } catch (e) {}
    };
    function scanRecentJoins() {
        if (window._joinPollPaused) return;
        try { captureMyUserUid(); } catch (e) {}
        try { collectPresenceFromDataIds(); } catch (e) {}
        const rows = cont.querySelectorAll('[data-id*="user/user-"], [class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], [class*="notification"], [class*="join"], li, div');
        const start = Math.max(0, rows.length - 80);
        for (let i = rows.length - 1; i >= start; i--) {
            const leave = parseLeaveRow(rows[i]);
            if (leave) { emitLeave(leave); continue; }
            const present = parsePresentRow(rows[i]);
            if (present) { emitPresent(present); continue; }
            const j = parseJoinRow(rows[i]);
            if (j) emitJoin(j);
        }
    }
    function findSystemInAddedNode(n) {
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
            const leave = parseLeaveRow(c);
            if (leave) return { kind: 'leave', item: leave };
            const present = parsePresentRow(c);
            if (present) return { kind: 'present', item: present };
            const j = parseJoinRow(c);
            if (j) return { kind: 'join', item: j };
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
        for (let i = rows.length - 1; i >= start; i--) {
            emitCommandFromRow(rows[i], null);
            emitChatFromRow(rows[i], null);
        }
    }
    if (window._o) { try { window._o.disconnect(); } catch(e){} }
    if (window._joinPoll) { clearInterval(window._joinPoll); window._joinPoll = null; }
    if (window._cmdPoll) { clearInterval(window._cmdPoll); window._cmdPoll = null; }
    window._o = new MutationObserver((ms) => {
        const batchRows = new Set();
        for (let m of ms) {
            for (let n of m.addedNodes) {
                if (n.nodeType !== 1 && n.nodeType !== 3) continue;
                const sys = findSystemInAddedNode(n);
                if (sys) {
                    if (sys.kind === 'leave') emitLeave(sys.item);
                    else if (sys.kind === 'present') emitPresent(sys.item);
                    else emitJoin(sys.item);
                    continue;
                }
                let el = n.nodeType === 3 ? n.parentElement : n;
                if (!el) continue;
                const row = el.closest ? el.closest('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="whisper"], [class*="system"], li') : el;
                if (!row || row === cont) continue;
                emitCommandFromRow(row, batchRows);
                emitChatFromRow(row, batchRows);
            }
        }
    });
    window._o.observe(cont, { childList: true, subtree: true, characterData: true });
    try { captureMyUserUid(); } catch (e) {}
    seedExistingJoins();
    seedExistingPresence();
    window._joinPoll = setInterval(scanRecentJoins, 2000);
    window._cmdPoll = setInterval(scanRecentCommands, 2000);
window._lastChatContainer = (root.hasStream ? 'chat-stream2' : 'body-fallback')
    + (root.hasInput ? '+input' : '') + ' | ' + (cont.className || cont.tagName);
window.__imvuSelfIdentity = function() {
    try { captureMyUserUid(); } catch (e) {}
    return '\t' + (window.__imvuSelfUid || '');
};
try { window.__imvuSelfIdentity(); } catch (e) {}
try { seedExistingPresence(); } catch (e) {}
