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
