function __imvuKickExtractUid(node) {
    if (!node || !node.getAttribute) return '';
    const attrs = [
        node.getAttribute('data-id'),
        node.getAttribute('data-userid'),
        node.getAttribute('data-user-id'),
        node.getAttribute('data-user')
    ];
    for (const v of attrs) {
        if (!v) continue;
        const m = String(v).match(/user\/user-(\d+)/i) || String(v).match(/user-(\d+)/i);
        if (m) return m[1];
    }
    return '';
}
function __imvuKickUidDeep(start) {
    let el = start;
    for (let d = 0; el && d < 16; d++) {
        const uid = __imvuKickExtractUid(el);
        if (uid) return uid;
        if (el.querySelector) {
            const hit = el.querySelector('[data-id*="user/user-"], [data-id*="user-"]');
            if (hit) {
                const u = __imvuKickExtractUid(hit);
                if (u) return u;
            }
        }
        el = el.parentElement;
    }
    return '';
}
function __imvuKickIsSelf(uid) {
    if (!uid) return false;
    if (window.__imvuSelfUid && String(window.__imvuSelfUid) === String(uid)) return true;
    const roots = [document];
    try {
        const r = typeof __imvuFindChatRoot === 'function' ? __imvuFindChatRoot() : null;
        if (r && r.doc) roots.push(r.doc);
    } catch (e) {}
    for (const doc of roots) {
        try {
            for (const n of doc.querySelectorAll('.my-user')) {
                const mine = __imvuKickUidDeep(n);
                if (mine && String(mine) === String(uid)) return true;
            }
        } catch (e) {}
    }
    return false;
}
function __imvuTakeChatId(s, into) {
    if (!s || !into) return;
    const re = /(?:\/chat\/)?(chat-\d+-\d+)/gi;
    let m;
    const str = String(s);
    while ((m = re.exec(str))) into.add(m[1]);
}
function __imvuWalkChatId(o, into, depth, seen) {
    if (!o || depth > 3 || !into) return;
    try {
        if (seen.has(o)) return;
        seen.add(o);
    } catch (e) { return; }
    if (typeof o === 'string') {
        __imvuTakeChatId(o, into);
        return;
    }
    if (typeof o !== 'object') return;
    try {
        if (typeof o.get === 'function') {
            const keys = ['id', 'chat_id', 'chatId', 'resource_name', 'resourceName', 'url'];
            for (const k of keys) {
                try { __imvuTakeChatId(o.get(k), into); } catch (e) {}
            }
        }
    } catch (e) {}
    const prefer = ['id', 'chat_id', 'chatId', 'resource_name', 'resourceName', 'url', 'href'];
    for (const k of prefer) {
        try { __imvuTakeChatId(o[k], into); } catch (e) {}
    }
    if (depth >= 2) return;
    try {
        const keys = Object.keys(o).slice(0, 40);
        for (const k of keys) {
            if (!/chat|room|id|resource|url|model|scene|policy/i.test(k)) continue;
            try { __imvuWalkChatId(o[k], into, depth + 1, seen); } catch (e) {}
        }
    } catch (e) {}
}
function __imvuSelfUid() {
    try {
        if (window.__imvuSelfUid) return String(window.__imvuSelfUid);
    } catch (e) {}
    try {
        if (window.top && window.top.__imvuSelfUid) return String(window.top.__imvuSelfUid);
    } catch (e) {}
    return '';
}
function __imvuFindChatResourceId() {
    const scored = [];
    const self = __imvuSelfUid();
    function add(id, score, time) {
        if (!id || !/^chat-\d+-\d+$/.test(id)) return;
        scored.push({ id: id, score: score || 0, time: time || 0 });
    }
    const wins = typeof __imvuAllWindows === 'function' ? __imvuAllWindows() : [window];
    for (const w of wins) {
        try {
            const href = w.location && w.location.href;
            const hm = href && String(href).match(/(chat-\d+-\d+)/);
            if (hm) add(hm[1], 25 + (self && hm[1].indexOf('chat-' + self + '-') === 0 ? 40 : 0), 0);
        } catch (e) {}
        try {
            const entries = w.performance && w.performance.getEntriesByType
                ? w.performance.getEntriesByType('resource') : [];
            for (const en of entries) {
                const name = (en && en.name) || '';
                const m = name.match(/\/chat\/(chat-\d+-\d+)/i);
                if (!m) continue;
                let s = 5;
                if (/\/participants/i.test(name)) s += 50;
                if (self && m[1].indexOf('chat-' + self + '-') === 0) s += 40;
                add(m[1], s, en.responseEnd || en.startTime || 0);
            }
        } catch (e) {}
    }
    const bag = new Set();
    try {
        const chat = (typeof __findActiveChat === 'function' && __findActiveChat())
            || window.__imvuCompanionActiveChat;
        __imvuWalkChatId(chat, bag, 0, new Set());
    } catch (e) {}
    const docs = typeof __imvuAllDocs === 'function' ? __imvuAllDocs() : [document];
    for (const doc of docs) {
        try {
            const nodes = doc.querySelectorAll('[data-chat-id], [data-id*="chat-"], [href*="chat-"], [src*="chat-"]');
            for (const n of nodes) {
                __imvuTakeChatId(n.getAttribute('data-chat-id'), bag);
                __imvuTakeChatId(n.getAttribute('data-id'), bag);
                __imvuTakeChatId(n.getAttribute('href'), bag);
                __imvuTakeChatId(n.getAttribute('src'), bag);
            }
        } catch (e) {}
    }
    for (const id of bag) add(id, 8 + (self && id.indexOf('chat-' + self + '-') === 0 ? 40 : 0), 0);
    scored.sort(function (a, b) { return b.score - a.score || b.time - a.time; });
    return scored.length ? scored[0].id : '';
}
function __imvuFindSauce() {
    const found = [];
    function fromCookie(c) {
        if (!c) return;
        const re = /(?:^|;\s*)(sauce|imvu_sauce|x-imvu-sauce)=([^;]+)/gi;
        let m;
        while ((m = re.exec(String(c)))) {
            try { found.push(decodeURIComponent(m[2])); } catch (e) { found.push(m[2]); }
        }
    }
    function fromStore(store) {
        if (!store) return;
        try {
            for (let i = 0; i < store.length; i++) {
                const k = store.key(i) || '';
                if (!/sauce/i.test(k)) continue;
                const v = store.getItem(k);
                if (v && v.length < 400) found.push(v);
            }
        } catch (e) {}
    }
    const wins = typeof __imvuAllWindows === 'function' ? __imvuAllWindows() : [window];
    for (const w of wins) {
        try { fromCookie(w.document && w.document.cookie); } catch (e) {}
        try { fromStore(w.localStorage); } catch (e) {}
        try { fromStore(w.sessionStorage); } catch (e) {}
        try {
            const I = w.IMVU;
            if (I) {
                if (I.sauce) found.push(String(I.sauce));
                if (I.client && I.client.sauce) found.push(String(I.client.sauce));
                if (typeof I.getSauce === 'function') {
                    try { found.push(String(I.getSauce())); } catch (e) {}
                }
            }
        } catch (e) {}
    }
    for (const s of found) {
        if (s && s !== 'undefined' && s !== 'null') return s;
    }
    return '';
}
function __imvuRequestWindows() {
    const wins = [];
    const seen = new Set();
    function add(w) {
        if (!w || seen.has(w)) return;
        seen.add(w);
        wins.push(w);
    }
    try {
        const root = typeof __imvuFindChatRoot === 'function' ? __imvuFindChatRoot() : null;
        if (root && root.doc && root.doc.defaultView) add(root.doc.defaultView);
    } catch (e) {}
    if (typeof __imvuAllWindows === 'function') {
        for (const w of __imvuAllWindows()) add(w);
    } else {
        add(window);
    }
    return wins;
}
function __imvuAwaitImvu(r, ms) {
    return new Promise(function (resolve) {
        let done = false;
        const finish = function (v) { if (done) return; done = true; resolve(v); };
        setTimeout(function () { finish('timeout'); }, ms || 2500);
        if (r == null) { finish('ok'); return; }
        try {
            if (typeof r.then === 'function') {
                r.then(function () { finish('ok'); }, function () { finish('rej'); });
                return;
            }
        } catch (e) {}
        try {
            if (typeof r.done === 'function') {
                r.done(function () { finish('ok'); }, function () { finish('rej'); });
                return;
            }
        } catch (e) {}
        finish('ok');
    });
}
function __imvuCollectHosts() {
    const hosts = [];
    const seen = new Set();
    function add(o) {
        if (!o || (typeof o !== 'object' && typeof o !== 'function')) return;
        try { if (seen.has(o)) return; seen.add(o); } catch (e) { return; }
        hosts.push(o);
    }
    const chat = (typeof __findActiveChat === 'function' && __findActiveChat())
        || window.__imvuCompanionActiveChat;
    add(chat);
    if (chat && typeof __chatRelatedRoots === 'function') {
        try { for (const r of __chatRelatedRoots(chat)) add(r); } catch (e) {}
    }
    const wins = typeof __imvuAllWindows === 'function' ? __imvuAllWindows() : [window];
    for (const w of wins) {
        try { add(w.IMVU); } catch (e) {}
        try { add(w.__imvuCompanionActiveChat); } catch (e) {}
        try {
            if (typeof w.bootFromChat === 'function') add(w);
            const keys = Object.keys(w);
            for (const k of keys) {
                let v;
                try { v = w[k]; } catch (e) { continue; }
                if (v && typeof v.bootFromChat === 'function') add(v);
                if (v && typeof v.delete === 'function' && typeof v.__request === 'function') add(v);
            }
        } catch (e) {}
    }
    const more = hosts.slice();
    for (const o of more) {
        try {
            const keys = Object.keys(o).slice(0, 80);
            for (const k of keys) {
                if (!/chat|room|boot|remove|http|request|client|gateway|api|policy|scene|dialog/i.test(k)) continue;
                try { add(o[k]); } catch (e) {}
            }
        } catch (e) {}
    }
    return hosts;
}
function __imvuFindHttpClients() {
    const hits = [];
    for (const o of __imvuCollectHosts()) {
        try {
            if (typeof o.delete !== 'function') continue;
            if (typeof o.__request === 'function' || typeof o.__queueRequest === 'function'
                || typeof o.__makeRequest === 'function' || typeof o.__pumpRequest === 'function')
                hits.push(o);
        } catch (e) {}
    }
    return hits;
}
async function __imvuRemoveViaBoot(uid, name) {
    const chat = (typeof __findActiveChat === 'function' && __findActiveChat())
        || window.__imvuCompanionActiveChat;
    let node = null;
    if (chat && typeof __resolveParticipantNode === 'function') {
        try {
            node = await Promise.race([
                __resolveParticipantNode(chat, uid, name || ''),
                new Promise(function (r) { setTimeout(function () { r(null); }, 1200); })
            ]);
        } catch (e) {}
    }
    const argsList = [];
    if (node) argsList.push([node]);
    argsList.push([uid], [Number(uid)], ['user-' + uid], [{ cid: uid, legacy_cid: uid }]);
    for (const o of __imvuCollectHosts()) {
        if (typeof o.bootFromChat !== 'function') continue;
        for (const args of argsList) {
            try {
                const r = o.bootFromChat.apply(o, args);
                const wait = await __imvuAwaitImvu(r, 2500);
                if (wait !== 'rej') return 'bootFromChat';
            } catch (e) {}
        }
    }
    return '';
}
async function __imvuRemoveViaImvuHttp(uid) {
    const chatId = __imvuFindChatResourceId();
    if (!chatId) return '';
    const paths = [
        'https://api.imvu.com/chat/' + chatId + '/participants/user-' + uid,
        '/chat/' + chatId + '/participants/user-' + uid,
        'chat/' + chatId + '/participants/user-' + uid
    ];
    const clients = __imvuFindHttpClients();
    for (const http of clients) {
        for (const path of paths) {
            try {
                const r = http.delete(path);
                const wait = await __imvuAwaitImvu(r, 2500);
                if (wait === 'ok') return 'imvu-http:' + chatId;
            } catch (e) {}
        }
    }
    return '';
}
async function __imvuRemoveViaApi(uid, name) {
    const boot = await __imvuRemoveViaBoot(uid, name);
    if (boot) return boot;
    const http = await __imvuRemoveViaImvuHttp(uid);
    if (http) return http;
    return 'no-imvu-delete';
}
function __imvuClickEl(el) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) {}
    if (typeof robustClick === 'function') return !!robustClick(el);
    if (typeof leftClickEl === 'function') return !!leftClickEl(el);
    try { el.click(); return true; } catch (e) { return false; }
}
function __imvuRightClickEl(el) {
    if (!el) return false;
    try { el.scrollIntoView({ block: 'nearest', inline: 'nearest' }); } catch (e) {}
    if (typeof fireMouse === 'function') {
        fireMouse(el, 'contextmenu', 2);
        fireMouse(el, 'mousedown', 2);
        fireMouse(el, 'mouseup', 2);
        return true;
    }
    try {
        el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2, view: window }));
        return true;
    } catch (e) { return false; }
}
function __imvuAvatarish(node) {
    if (!node) return null;
    if (node.querySelector) {
        const av = node.querySelector('[class*="avatar"], img, [class*="thumb"], button, [role="button"]');
        if (av) return av;
        const first = node.firstElementChild;
        if (first) return first;
    }
    return node;
}
function __imvuMessageBodyTarget(row) {
    if (!row) return null;
    let best = null;
    let bestLen = 0;
    try {
        const nodes = row.querySelectorAll('div, span, p');
        for (const d of nodes) {
            if (d.querySelector && d.querySelector('img, [class*="avatar"]')) continue;
            const t = String(d.innerText || d.textContent || '').replace(/\s+/g, ' ').trim();
            if (t.length < 1 || t.length > 240) continue;
            if (t.length >= bestLen) {
                best = d;
                bestLen = t.length;
            }
        }
    } catch (e) {}
    return best || row;
}
function __imvuCollectUserClickTargets(uid, name) {
    const out = [];
    const seen = new Set();
    function push(el) {
        if (!el || seen.has(el)) return;
        seen.add(el);
        out.push(el);
    }
    const needle = 'user/user-' + uid;
    const docs = typeof __imvuAllDocs === 'function' ? __imvuAllDocs() : [document];
    for (const doc of docs) {
        let nodes = [];
        try { nodes = Array.from(doc.querySelectorAll('[data-id*="' + needle + '"]')); } catch (e) {}
        if (!nodes.length) {
            try { nodes = Array.from(doc.querySelectorAll('[data-id*="user-' + uid + '"]')); } catch (e) {}
        }
        for (let i = nodes.length - 1; i >= 0; i--) {
            const row = nodes[i];
            if (__imvuKickIsSelf(__imvuKickExtractUid(row) || uid)) continue;
            push(__imvuMessageBodyTarget(row));
            push(row);
        }
    }
    if (typeof resolveJoinWrapper === 'function' && typeof joinAvatarClickTarget === 'function') {
        try {
            const wrap = resolveJoinWrapper('', uid);
            if (wrap) push(joinAvatarClickTarget(wrap));
        } catch (e) {}
    }
    if (name && typeof findUserTarget === 'function') {
        try { push(findUserTarget(name)); } catch (e) {}
    }
    return out;
}
function __imvuFindRemoveUserMenuItem() {
    const roots = typeof allSearchRoots === 'function' ? allSearchRoots() : [document];
    for (const root of roots) {
        try {
            const hit = root.querySelector('li[data-menu-item="remove_user"], [data-menu-item="remove_user"]');
            if (hit) return hit;
        } catch (e) {}
    }
    return null;
}
function __imvuFindRemoveConfirm() {
    const roots = typeof allSearchRoots === 'function' ? allSearchRoots() : [document];
    const labelOf = typeof elementOwnLabel === 'function'
        ? elementOwnLabel
        : function (el) { return ((el && el.textContent) || '').replace(/\s+/g, ' ').trim(); };
    for (const root of roots) {
        let nodes;
        try { nodes = root.querySelectorAll('button, [role="button"], [type="submit"]'); } catch (e) { continue; }
        for (const el of nodes) {
            try {
                const menu = (el.getAttribute && el.getAttribute('data-menu-item')) || '';
                if (menu === 'remove_user') continue;
                const own = (labelOf(el) || '').replace(/\s+/g, ' ').trim();
                const text = String(el.textContent || '').replace(/\s+/g, ' ').trim();
                if (!/^remove$/i.test(own) && !/^remove$/i.test(text)) continue;
                const r = el.getBoundingClientRect();
                if (r.width <= 0 || r.height <= 0) continue;
                return el;
            } catch (e) {}
        }
    }
    return null;
}
function __imvuWait(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
}
function __imvuPollUntil(fn, tries, gap) {
    return new Promise(function (resolve) {
        let n = 0;
        const tick = function () {
            const hit = fn();
            if (hit) { resolve(hit); return; }
            if (++n >= tries) { resolve(null); return; }
            setTimeout(tick, gap);
        };
        tick();
    });
}
async function __imvuRemoveViaUi(uid, name) {
    let item = __imvuFindRemoveUserMenuItem();
    if (!item) {
        const targets = __imvuCollectUserClickTargets(uid, name || '').slice(0, 3);
        if (!targets.length) return 'not-found';
        for (const node of targets) {
            __imvuClickEl(node);
            item = await __imvuPollUntil(__imvuFindRemoveUserMenuItem, 12, 70);
            if (item) break;
        }
    }
    if (!item) {
        const dbg = typeof getMenuItemsDebug === 'function' ? getMenuItemsDebug() : '';
        return 'no-remove-item' + (dbg ? ':' + dbg : '');
    }
    if (!__imvuClickEl(item)) return 'click-failed';
    const confirm = await __imvuPollUntil(__imvuFindRemoveConfirm, 25, 80);
    if (!confirm) return 'no-confirm';
    if (!__imvuClickEl(confirm)) return 'confirm-failed';
    return 'ui:confirmed';
}
async function __imvuRemoveUserTry(uid, name) {
    uid = String(uid || '').trim();
    if (!uid) return 'no-uid';
    if (__imvuKickIsSelf(uid)) return 'self';
    const api = await __imvuRemoveViaApi(uid, name);
    if (api === 'bootFromChat' || (api && api.indexOf('imvu-http:') === 0))
        return api;
    if (api && api.indexOf('api:') === 0 && api.indexOf('api-fail') !== 0 && api.indexOf('api-error') !== 0)
        return api;
    const ui = await __imvuRemoveViaUi(uid, name || '');
    if (ui === 'ui:confirmed') return ui;
    return (api && api !== 'no-chat-id' ? api + '|' : '') + ui;
}
function __imvuRemoveUserStart(uid, name) {
    window.__imvuRemoveResult = 'pending';
    Promise.resolve()
        .then(function () { return __imvuRemoveUserTry(uid, name); })
        .then(function (r) {
            window.__imvuRemoveResult = (r == null || r === '') ? 'empty-result' : String(r);
        })
        .catch(function (e) {
            window.__imvuRemoveResult = 'exception:' + (e && e.message ? e.message : String(e));
        });
    return 'started';
}
function __imvuRemoveUserPoll() {
    return window.__imvuRemoveResult || '';
}
