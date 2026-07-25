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
