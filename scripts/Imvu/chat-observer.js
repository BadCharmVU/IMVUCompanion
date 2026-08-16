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
        const s = (name || '').replace(/[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]/g, '').trim();
        return /[\p{L}\p{N}]/u.test(s);
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
        const m = norm(text).match(nameRx);
        let name = m ? norm(m[1]) : '';
        if (!name) name = nameFromJoinAvatarImg(row);
        name = norm(name);
        if (!hasVisibleName(name) || pred(name) || isJoinText(name)) return null;
        return { name, text, row };
    }
    function parseLeaveRow(row) { return parseNamedSystemRow(row, isLeaveText, leaveNameRx); }
    function parsePresentRow(row) { return parseNamedSystemRow(row, isPresentText, presentNameRx); }
    function isSelfIdentity(name, uid) {
        const selfUid = window.__imvuSelfUid || '';
        const selfName = (window.__imvuSelfName || '').replace(/\s+/g, ' ').trim().toLowerCase();
        if (uid && selfUid && String(uid) === String(selfUid)) return true;
        const n = (name || '').replace(/\s+/g, ' ').trim().toLowerCase();
        return !!(n && selfName && n === selfName);
    }
    function emitTyped(j, kind) {
        if (!j || !j.row || !j.name) return;
        const wrapper = getSystemRowWrapper(j.row, kind) || j.row;
        const seenKey = kind === 'leave' ? '_seenLeaveRows' : '_seenPresentRows';
        if (!window[seenKey]) window[seenKey] = new WeakSet();
        const userId = extractUserIdDeep(wrapper) || extractUserIdDeep(j.row);
        let prevUid = '';
        try { prevUid = wrapper.getAttribute('data-imvu-bot-user-id') || ''; } catch(e) {}
        if (window[seenKey].has(wrapper) && (prevUid || !userId)) return;
        if (isSelfIdentity(j.name, userId)) return;
        window[seenKey].add(wrapper);
        let rowRef = kind.charAt(0) + Date.now() + '_' + Math.random().toString(36).slice(2, 7);
        try {
            wrapper.setAttribute('data-imvu-bot-' + kind, rowRef);
            if (userId) wrapper.setAttribute('data-imvu-bot-user-id', userId);
        } catch(e) {}
        post(j.name + "\t" + j.text + "\t" + kind + "\t" + rowRef + "\t" + (userId || ''));
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
            const lines = raw.split(/[\n\r]+/).map(l => norm(l)).filter(Boolean);
            for (const line of lines) {
                if (!isPresentText(line)) continue;
                const m = line.match(presentNameRx);
                const name = m ? norm(m[1]) : '';
                if (name && hasVisibleName(name))
                    emitPresent({ name, text: line, row: n });
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
        try { seedExistingPresence(); } catch (e) {}
    };
    function scanRecentJoins() {
        if (window._joinPollPaused) return;
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
    seedExistingJoins();
    seedExistingPresence();
    window._joinPoll = setInterval(scanRecentJoins, 2000);
    window._cmdPoll = setInterval(scanRecentCommands, 2000);
window._lastChatContainer = (root.hasStream ? 'chat-stream2' : 'body-fallback')
    + (root.hasInput ? '+input' : '') + ' | ' + (cont.className || cont.tagName);
window.__imvuSelfIdentity = function() {
    function fold(s) { return (s || '').replace(/\s+/g, ' ').trim(); }
    function uidFrom(v) {
        const s = String(v == null ? '' : v);
        const m = s.match(/user\/user-(\d+)/i) || s.match(/\b(\d{6,})\b/);
        return m ? m[1] : '';
    }
    function nameFrom(v) {
        if (v == null) return '';
        if (typeof v === 'string') return fold(v);
        if (typeof v !== 'object') return '';
        const keys = ['displayName', 'display_name', 'avatarName', 'userName', 'username', 'name'];
        for (const k of keys) {
            try {
                if (typeof v[k] === 'string' && v[k].trim()) return fold(v[k]);
                if (typeof v.get === 'function') {
                    const g = v.get(k);
                    if (typeof g === 'string' && g.trim()) return fold(g);
                }
            } catch (e) {}
        }
        return '';
    }
    function harvest(obj, depth) {
        if (!obj || typeof obj !== 'object' || depth > 3) return;
        const nameKeys = ['avatarName', 'displayName', 'display_name', 'userName', 'username', 'loggedInUserName'];
        const uidKeys = ['legacy_cid', 'cid', 'userId', 'user_id', 'customerId', 'customer_id'];
        const nest = ['user', 'self', 'me', 'currentUser', 'avatar', 'loggedInUser', 'customer'];
        for (const k of nameKeys) {
            if (window.__imvuSelfName) break;
            try {
                const v = typeof obj.get === 'function' ? obj.get(k) : obj[k];
                const n = nameFrom(v);
                if (n && n.length <= 60) window.__imvuSelfName = n;
            } catch (e) {}
        }
        for (const k of uidKeys) {
            if (window.__imvuSelfUid) break;
            try {
                const v = typeof obj.get === 'function' ? obj.get(k) : obj[k];
                const u = uidFrom(v);
                if (u) window.__imvuSelfUid = u;
            } catch (e) {}
        }
        for (const k of nest) {
            try {
                const v = typeof obj.get === 'function' ? obj.get(k) : obj[k];
                harvest(v, depth + 1);
            } catch (e) {}
        }
    }
    const chat = window.__imvuCompanionActiveChat
        || (window.top && window.top.__imvuCompanionActiveChat);
    harvest(chat, 0);
    try { harvest(window.IMVU, 0); } catch (e) {}
    try { harvest(window.IMVU && window.IMVU.Client, 0); } catch (e) {}
    return (window.__imvuSelfName || '') + '\t' + (window.__imvuSelfUid || '');
};
try { window.__imvuSelfIdentity(); } catch (e) {}
try { seedExistingPresence(); } catch (e) {}
