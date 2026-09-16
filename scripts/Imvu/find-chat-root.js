function __imvuRoomKey(raw) {
    if (!raw) return '';
    const m = String(raw).match(/room-\d[\w-]*/i);
    return m ? m[0].toLowerCase() : '';
}

function __imvuIsMiniChrome(el) {
    if (!el || !el.closest) return false;
    try {
        return !!el.closest('.window-manager, .window-wrapper.chat-window, .chat-window');
    } catch (e) { return false; }
}

function __imvuInstallRoomTap() {
    if (window.__imvuRoomTap) return;
    window.__imvuRoomTap = true;
    window.__imvuRoomIdSeen = window.__imvuRoomIdSeen || [];
    const seen = new Set(window.__imvuRoomIdSeen);
    function note(s) {
        const k = __imvuRoomKey(s);
        if (!k || seen.has(k)) return;
        seen.add(k);
        window.__imvuRoomIdSeen.push(k);
        window.__imvuLastRoomId = k;
    }
    window.__imvuNoteRoomId = note;

    function noteJoin(data) {
        if (!data || typeof data !== 'object') return;
        try { note(data.roomId || data.room_id || ''); } catch (e) {}
        try {
            const n = String(data.roomName || data.room_name || data.name || '').replace(/\s+/g, ' ').trim();
            if (n && n.length < 80 && n.toLowerCase() !== 'home')
                window.__imvuLastRoomName = n;
        } catch (e) {}
    }

    function wrapClever(obj, key) {
        try {
            const orig = obj[key];
            if (typeof orig !== 'function' || orig.__imvuWrapped) return false;
            const wrapped = function () {
                try {
                    const ev = arguments[0];
                    const payload = arguments[1];
                    const name = typeof ev === 'string' ? ev
                        : (ev && (ev.event || ev.name || ev.type)) || '';
                    const data = (payload && typeof payload === 'object') ? payload
                        : (ev && typeof ev === 'object' ? ev : null);
                    if (/join_chat_room|leave_chat_room/i.test(String(name)))
                        noteJoin(data || {});
                    else if (data && (data.roomId || data.roomName))
                        noteJoin(data);
                } catch (e) {}
                return orig.apply(this, arguments);
            };
            wrapped.__imvuWrapped = true;
            obj[key] = wrapped;
            return true;
        } catch (e) { return false; }
    }

    function hookCleverTap() {
        wrapClever(window, 'recordClevertapEvent');
        try { if (window.clevertap && window.clevertap.event) wrapClever(window.clevertap.event, 'push'); } catch (e) {}
        try { if (window.IMVU) wrapClever(window.IMVU, 'recordClevertapEvent'); } catch (e) {}
    }
    hookCleverTap();
    let n = 0;
    const t = setInterval(function () {
        hookCleverTap();
        if (++n > 40) clearInterval(t);
    }, 500);

    try {
        const po = new PerformanceObserver(function (list) {
            for (const e of list.getEntries()) note(e.name);
        });
        po.observe({ type: 'resource', buffered: true });
    } catch (e) {
        try {
            const po = new PerformanceObserver(function (list) {
                for (const e of list.getEntries()) note(e.name);
            });
            po.observe({ entryTypes: ['resource'] });
        } catch (e2) {}
    }

    try {
        const origFetch = window.fetch;
        if (typeof origFetch === 'function' && !origFetch.__imvuWrapped) {
            const wrappedFetch = function (input) {
                try {
                    const url = String((input && input.url) || input || '');
                    note(url);
                    if (/\/conversation/i.test(url))
                        window.__imvuLastConversationUrl = url;
                } catch (e) {}
                return origFetch.apply(this, arguments);
            };
            wrappedFetch.__imvuWrapped = true;
            window.fetch = wrappedFetch;
        }
    } catch (e) {}
}

function __imvuHarvestRoomStrings(note) {
    function walkObj(o, depth, seen) {
        if (!o || depth > 4) return;
        try {
            if (seen.has(o)) return;
            seen.add(o);
        } catch (e) { return; }
        const t = typeof o;
        if (t === 'string') { note(o); return; }
        if (t !== 'object') return;
        try {
            if (typeof o.get === 'function') {
                for (const k of ['id', 'room_id', 'roomId', 'room', 'resource_name', 'resourceName', 'url', 'href']) {
                    try { note(o.get(k)); } catch (e) {}
                }
            }
        } catch (e) {}
        const prefer = ['id', 'room_id', 'roomId', 'room', 'scene_id', 'sceneId', 'instance_id', 'resource_name', 'resourceName', 'url', 'href', 'cid'];
        for (const k of prefer) {
            try { note(o[k]); } catch (e) {}
        }
        if (depth >= 3) return;
        try {
            const keys = Object.keys(o).slice(0, 50);
            for (const k of keys) {
                if (!/room|scene|chat|id|resource|url|model|instance|location/i.test(k)) continue;
                try { walkObj(o[k], depth + 1, seen); } catch (e) {}
            }
        } catch (e) {}
    }

    const wins = [window];
    try { if (window.top && window.top !== window) wins.push(window.top); } catch (e) {}
    for (const w of wins) {
        try {
            note(w.location && w.location.href);
            const entries = w.performance && w.performance.getEntriesByType
                ? w.performance.getEntriesByType('resource') : [];
            for (const en of entries) note(en && en.name);
        } catch (e) {}
        try { walkObj(w.__imvuCompanionActiveChat, 0, new Set()); } catch (e) {}
        try {
            const seen = w.__imvuRoomIdSeen;
            if (seen && seen.length) for (const k of seen) note(k);
        } catch (e) {}
        try { note(w.__imvuLastRoomId); } catch (e) {}
    }

    try {
        const chat = (typeof __findActiveChat === 'function' && __findActiveChat())
            || window.__imvuCompanionActiveChat;
        walkObj(chat, 0, new Set());
    } catch (e) {}
}

function __imvuWalkDocs(fn) {
    const seen = new Set();
    function walkDoc(doc) {
        if (!doc || seen.has(doc)) return;
        seen.add(doc);
        try { fn(doc); } catch (e) {}
        try {
            const all = doc.querySelectorAll('*');
            for (const el of all) {
                if (el.shadowRoot) {
                    try { fn(el.shadowRoot); } catch (e) {}
                    try { walkDoc(el.shadowRoot); } catch (e) {}
                }
            }
        } catch (e) {}
        try {
            for (const frame of doc.querySelectorAll('iframe')) {
                try {
                    const fd = frame.contentDocument || frame.contentWindow?.document;
                    if (fd) walkDoc(fd);
                } catch (e) {}
            }
        } catch (e) {}
    }
    walkDoc(document);
}

function __imvuCountMiniWindows() {
    let n = 0;
    __imvuWalkDocs(root => {
        if (!root.querySelectorAll) return;
        try {
            n += root.querySelectorAll('.window-manager .window-wrapper.chat-window').length;
        } catch (e) {}
        if (n === 0) {
            try {
                n += root.querySelectorAll('.window-manager .window.window-pulse, .window-manager .chat-window .window').length;
            } catch (e) {}
        }
    });
    return n;
}

function __imvuCollectRoomSnapshot() {
    __imvuInstallRoomTap();
    const minimized = [];
    const minSet = new Set();
    const extra = [];
    const extraSet = new Set();
    const harvested = [];
    const harvestSet = new Set();

    function take(raw, mini) {
        const key = __imvuRoomKey(raw);
        if (!key) return '';
        if (typeof window.__imvuNoteRoomId === 'function') window.__imvuNoteRoomId(raw);
        if (mini) {
            if (!minSet.has(key)) { minSet.add(key); minimized.push(key); }
        } else if (!extraSet.has(key) && !minSet.has(key)) {
            extraSet.add(key);
            extra.push(key);
        }
        if (!harvestSet.has(key)) { harvestSet.add(key); harvested.push(key); }
        return key;
    }

    __imvuHarvestRoomStrings(s => take(s, false));

    __imvuWalkDocs(root => {
        if (!root.querySelectorAll) return;
        let nodes;
        try { nodes = root.querySelectorAll('[data-id], [href], [src], [data-room], [data-room-id], [data-roomid]'); }
        catch (e) { return; }
        for (const el of nodes) {
            if (!el.getAttribute) continue;
            const raw = el.getAttribute('data-id')
                || el.getAttribute('href')
                || el.getAttribute('src')
                || el.getAttribute('data-room')
                || el.getAttribute('data-room-id')
                || el.getAttribute('data-roomid')
                || '';
            if (!/room-\d/i.test(raw)) continue;
            take(raw, __imvuIsMiniChrome(el));
        }
        try {
            const managers = root.querySelectorAll('.window-manager');
            for (const m of managers) take(m.innerHTML || '', true);
        } catch (e) {}
    });

    const miniCount = __imvuCountMiniWindows();
    const root = __imvuFindRestoredChat();
    const restoredOpen = !!(root && root.hasStream && root.hasInput);
    let restoredId = '';
    if (restoredOpen) {
        const probes = [];
        try { probes.push(root.doc.location && root.doc.location.href); } catch (e) {}
        try { probes.push(location.href); } catch (e) {}
        try {
            let n = root.cont;
            for (let i = 0; n && i < 12; i++) {
                if (n.getAttribute) {
                    probes.push(n.getAttribute('data-id') || n.getAttribute('href') || '');
                    if (n.attributes) {
                        for (const a of n.attributes) probes.push(a.value);
                    }
                }
                n = n.parentElement;
            }
        } catch (e) {}
        try {
            root.doc.querySelectorAll('[data-id], [href]').forEach(el => {
                if (__imvuIsMiniChrome(el)) return;
                probes.push(el.getAttribute('data-id') || el.getAttribute('href') || '');
            });
        } catch (e) {}
        for (const p of probes) {
            const k = __imvuRoomKey(p);
            if (k && !minSet.has(k)) { restoredId = k; break; }
        }
        if (!restoredId) {
            for (const p of probes) {
                const k = __imvuRoomKey(p);
                if (k) { restoredId = k; break; }
            }
        }
        if (!restoredId && extra.length) restoredId = extra[extra.length - 1];
        if (!restoredId) {
            try { restoredId = window.__imvuLastRoomId || ''; } catch (e) {}
        }
    }

    if (!minimized.length && miniCount > 0) {
        try {
            const last = window.__imvuLastRoomId || '';
            if (__imvuRoomKey(last) && !minSet.has(last)) {
                minSet.add(last);
                minimized.push(last);
            }
        } catch (e) {}
        if (!minimized.length && harvested.length === 1) {
            minimized.push(harvested[0]);
            minSet.add(harvested[0]);
        }
    }

    const ids = minimized.slice();
    if (restoredId && !minSet.has(restoredId)) ids.push(restoredId);

    let lastRoomId = '';
    try { lastRoomId = window.__imvuLastRoomId || ''; } catch (e) {}

    return {
        restoredOpen,
        restoredId,
        minimized,
        ids,
        miniCount,
        lastRoomId,
        roomName: (function () {
            try {
                const live = window.__imvuLastRoomName;
                if (live && String(live).trim()) return String(live).trim();
            } catch (e) {}
            return __imvuReadRoomName();
        })(),
        count: ids.length
    };
}

function __imvuIsNavJunkName(t) {
    t = (t || '').replace(/\s+/g, ' ').trim();
    if (!t || t.length > 80) return true;
    return /^(home|chat|chats|shop|discover|explore|messages|message|notifications|profile|settings|imvu|hangout|hangouts|chat now|next|menu)$/i.test(t);
}

function __imvuReadRoomName() {
    const found = [];
    function add(t) {
        t = (t || '').replace(/\s+/g, ' ').trim();
        if (!t || __imvuIsNavJunkName(t)) return;
        if (/^room-\d/i.test(t)) return;
        if (found.indexOf(t) < 0) found.push(t);
    }
    try { add(window.__imvuLastRoomName); } catch (e) {}
    function firstLine(el) {
        if (!el) return '';
        return (el.innerText || el.textContent || '').split(/\r?\n/).map(s => s.trim()).find(s => s) || '';
    }
    __imvuWalkDocs(root => {
        if (!root.querySelectorAll) return;
        const exact = root.querySelectorAll(
            'div.mode-nav.bar.magic-line-menu.disable-text-selection .center-nav-container.has-subnav-with-title ul.center-nav > li'
        );
        for (const li of exact) add(li.textContent);
        const boxes = root.querySelectorAll('.center-nav-container.has-subnav-with-title');
        for (const box of boxes) {
            for (const el of box.querySelectorAll('li, span, a, div, p')) add(el.textContent);
        }
        const extra = root.querySelectorAll(
            '[class*="room-title"], [class*="roomName"], [class*="chat-title"], [class*="hangout-title"], [class*="subnav-with-title"]'
        );
        for (const el of extra) add(firstLine(el));
        // Hangout/minimized chip title — often the only place the name exists
        for (const el of root.querySelectorAll('.window-manager .window, .window-wrapper.chat-window, .window.window-pulse'))
            add(firstLine(el));
    });
    return found.length ? found[found.length - 1] : '';
}

// Live restored chat only — never the minimized window-manager chips.
function __imvuFindRestoredChat() {
    function findInDoc(doc) {
        const conts = doc.querySelectorAll('div.chat-stream2, [class*="chat-stream2"]');
        let cont = null;
        for (const c of conts) {
            if (!__imvuIsMiniChrome(c)) { cont = c; break; }
        }
        const inps = doc.querySelectorAll('div.input-container, [class*="input-container"]');
        let inp = null;
        for (const i of inps) {
            if (!__imvuIsMiniChrome(i)) { inp = i; break; }
        }
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

function __imvuFindChatRoot() {
    try { __imvuInstallRoomTap(); } catch (e) {}
    return __imvuFindRestoredChat();
}
