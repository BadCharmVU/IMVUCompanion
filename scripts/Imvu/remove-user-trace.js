function __imvuRemoveTraceInteresting(s) {
    return /remove|kick|eject|boot|ban|moderat|participant|occupant|legacy_cid|user\/user-/i.test(String(s || ''));
}
function __imvuRemoveTraceRec(kind, data) {
    if (!window.__imvuRemoveLog) window.__imvuRemoveLog = [];
    const row = { t: Date.now(), kind: kind, data: data };
    window.__imvuRemoveLog.push(row);
    if (window.__imvuRemoveLog.length > 200) window.__imvuRemoveLog.shift();
    try { console.log('%c[REMOVE-TRACE] ' + kind, 'color:#f87171;font-weight:bold', data); } catch (e) {}
    return row;
}
function __imvuRemoveTraceSafe(v) {
    if (v == null) return v;
    if (typeof v === 'string' || typeof v === 'number' || typeof v === 'boolean') return v;
    try { return JSON.parse(JSON.stringify(v)); } catch (e) {}
    try { return String(v).slice(0, 400); } catch (e2) { return '[unprintable]'; }
}
function __imvuRemoveTraceHookWindow(w) {
    if (!w || w.__imvuRemoveTraceHooked) return;
    try { w.__imvuRemoveTraceHooked = true; } catch (e) { return; }

    try {
        const ofetch = w.fetch;
        if (typeof ofetch === 'function') {
            w.fetch = function () {
                let url = '', body = '';
                try {
                    url = String(arguments[0] && arguments[0].url ? arguments[0].url : arguments[0]);
                    if (arguments[1] && arguments[1].body) body = String(arguments[1].body);
                } catch (e) {}
                if (__imvuRemoveTraceInteresting(url + ' ' + body))
                    __imvuRemoveTraceRec('fetch', { url: url, body: body.slice(0, 2000) });
                return ofetch.apply(this, arguments).then(function (res) {
                    if (__imvuRemoveTraceInteresting(url + ' ' + (res && res.url))) {
                        try {
                            res.clone().text().then(function (t) {
                                __imvuRemoveTraceRec('fetch-res', {
                                    url: res.url, status: res.status, body: String(t || '').slice(0, 2000)
                                });
                            });
                        } catch (e) {}
                    }
                    return res;
                });
            };
        }
    } catch (e) {}

    try {
        const XO = w.XMLHttpRequest;
        if (XO && XO.prototype && !XO.prototype.__imvuRtOpen) {
            const open = XO.prototype.open;
            const send = XO.prototype.send;
            XO.prototype.__imvuRtOpen = true;
            XO.prototype.open = function (m, u) {
                this.__imvuRt = { m: m, u: String(u) };
                return open.apply(this, arguments);
            };
            XO.prototype.send = function (b) {
                const info = this.__imvuRt || {};
                const body = b == null ? '' : String(b);
                if (__imvuRemoveTraceInteresting((info.u || '') + ' ' + body))
                    __imvuRemoveTraceRec('xhr', { method: info.m, url: info.u, body: body.slice(0, 2000) });
                return send.apply(this, arguments);
            };
        }
    } catch (e) {}

    try {
        const WS = w.WebSocket;
        if (WS && WS.prototype && !WS.prototype.__imvuRtSend) {
            const wssend = WS.prototype.send;
            WS.prototype.__imvuRtSend = true;
            WS.prototype.send = function (d) {
                let s = '';
                try {
                    s = typeof d === 'string' ? d : (d && d.byteLength ? '[bin ' + d.byteLength + ']' : String(d));
                } catch (e) { s = '[ws]'; }
                if (__imvuRemoveTraceInteresting(s))
                    __imvuRemoveTraceRec('ws-send', s.slice(0, 2000));
                return wssend.apply(this, arguments);
            };
        }
    } catch (e) {}

    try {
        const doc = w.document;
        if (doc && !doc.__imvuRtClick) {
            doc.__imvuRtClick = true;
            doc.addEventListener('click', function (e) {
                let el = e.target;
                for (let i = 0; el && i < 8; i++, el = el.parentElement) {
                    let menu = '', action = '', testid = '', aria = '', cls = '', text = '';
                    try {
                        menu = (el.getAttribute && el.getAttribute('data-menu-item')) || '';
                        action = (el.getAttribute && el.getAttribute('data-action')) || '';
                        testid = (el.getAttribute && el.getAttribute('data-testid')) || '';
                        aria = (el.getAttribute && el.getAttribute('aria-label')) || '';
                        cls = String(el.className || '').slice(0, 160);
                        text = String(el.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80);
                    } catch (err) {}
                    const blob = [menu, action, testid, aria, text].join(' ');
                    if (__imvuRemoveTraceInteresting(blob) && blob.length < 200) {
                        __imvuRemoveTraceRec('click', {
                            tag: el.tagName, cls: cls, menu: menu, action: action,
                            testid: testid, aria: aria, text: text,
                            html: String(el.outerHTML || '').slice(0, 500)
                        });
                        break;
                    }
                }
            }, true);
        }
    } catch (e) {}
}
function __imvuRemoveTraceWrapFns(o, prefix, depth) {
    if (!o || depth > 2) return;
    let keys = [];
    try { keys = Object.keys(o); } catch (e) {}
    try { keys = keys.concat(Object.getOwnPropertyNames(o)); } catch (e) {}
    const seen = {};
    for (let i = 0; i < keys.length; i++) {
        const k = keys[i];
        if (!k || seen[k]) continue;
        seen[k] = 1;
        let v;
        try { v = o[k]; } catch (e) { continue; }
        if (typeof v === 'function' && __imvuRemoveTraceInteresting(k) && !v.__imvuRt) {
            const orig = v;
            const name = prefix + k;
            const wrapped = function () {
                const args = [];
                for (let a = 0; a < arguments.length; a++) args.push(__imvuRemoveTraceSafe(arguments[a]));
                __imvuRemoveTraceRec('call', { name: name, args: args });
                return orig.apply(this, arguments);
            };
            wrapped.__imvuRt = true;
            try { o[k] = wrapped; } catch (e) {}
        } else if (v && typeof v === 'object' && __imvuRemoveTraceInteresting(k)) {
            __imvuRemoveTraceWrapFns(v, prefix + k + '.', depth + 1);
        }
    }
}
function __imvuRemoveTraceInstall() {
    if (window.__imvuRemoveTraceOn) return 'already';
    window.__imvuRemoveTraceOn = true;
    window.__imvuRemoveLog = window.__imvuRemoveLog || [];
    const wins = typeof __imvuAllWindows === 'function' ? __imvuAllWindows() : [window];
    for (const w of wins) __imvuRemoveTraceHookWindow(w);
    try {
        const chat = (typeof __findActiveChat === 'function' && __findActiveChat())
            || window.__imvuCompanionActiveChat
            || (window.top && window.top.__imvuCompanionActiveChat);
        if (chat) {
            __imvuRemoveTraceWrapFns(chat, 'chat.', 0);
            const names = [];
            try {
                for (const k of Object.keys(chat)) {
                    if (typeof chat[k] === 'function') names.push(k);
                }
            } catch (e) {}
            __imvuRemoveTraceRec('chat-fns', names.sort().join(','));
        } else {
            __imvuRemoveTraceRec('chat-fns', 'no-active-chat');
        }
    } catch (e) {
        __imvuRemoveTraceRec('chat-fns', 'err:' + (e && e.message ? e.message : e));
    }
    __imvuRemoveTraceRec('ready', 'do Remove User in IMVU now');
    return 'ok';
}
function __imvuRemoveTraceDump() {
    try { return JSON.stringify(window.__imvuRemoveLog || []); } catch (e) { return '[]'; }
}
function __imvuRemoveTraceSince(n) {
    const log = window.__imvuRemoveLog || [];
    const start = Math.max(0, Number(n) || 0);
    try { return JSON.stringify(log.slice(start)); } catch (e) { return '[]'; }
}
