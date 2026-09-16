function __imvuOpenNativeDm(uid, displayName) {
    try {
        return __imvuOpenNativeDmInner(uid, displayName);
    } catch (e) {
        return 'js-err:' + (e && e.message ? e.message : String(e));
    }
}

function __imvuLiveTargetUser(cid) {
    const want = String(cid);
    const roots = [];
    try { roots.push(window.__imvuCompanionActiveChat); } catch (e) {}
    try {
        const sv = window.__imvuServices;
        if (sv) for (const v of Object.values(sv)) roots.push(v);
    } catch (e) {}
    function cidOf(node) {
        try { const a = node.get && node.get('legacy_cid'); if (a != null) return String(a); } catch (e) {}
        try { if (node.legacy_cid != null) return String(node.legacy_cid); } catch (e) {}
        return '';
    }
    function lookup(o) {
        if (!o) return null;
        try {
            if (typeof o.__getParticipantNodeByLegacyCid === 'function') {
                const n = o.__getParticipantNodeByLegacyCid(cid) || o.__getParticipantNodeByLegacyCid(want);
                if (n) return n;
            }
        } catch (e) {}
        try {
            const coll = o.__participants || o.participants;
            const models = (coll && coll.models) || coll || [];
            if (models && models.length) {
                for (const m of models) {
                    if (cidOf(m) === want) return m;
                }
            }
        } catch (e) {}
        return null;
    }
    for (const root of roots) {
        let n = lookup(root);
        if (n) return n;
        try {
            for (const k of Object.keys(root || {})) {
                try {
                    n = lookup(root[k]);
                    if (n) return n;
                } catch (e) {}
            }
        } catch (e) {}
    }
    return null;
}

function __imvuInspectDmDock(cid) {
    let wins = 0, input = 0, body = 0, pulse = 0;
    const ids = [];
    try {
        const nodes = document.querySelectorAll('.window-manager .window, .window-manager .window-wrapper');
        wins = nodes.length;
        for (const w of nodes) {
            const did = (w.getAttribute && w.getAttribute('data-id')) || '';
            if (did) ids.push(did.replace(/^https:\/\/api\.imvu\.com\//, ''));
            if (w.classList.contains('window-pulse') || w.classList.contains('animate-up')) pulse++;
            if (w.querySelector('textarea, [contenteditable="true"], .input-container'))
                input++;
            if (w.querySelector('[class*="conversation-body"], [class*="message-list"], [class*="chat-stream"]'))
                body++;
        }
    } catch (e) {}
    return 'wins=' + wins + ' body=' + body + ' input=' + input + ' pulse=' + pulse +
        ' ids=' + ids.slice(0, 4).join('|');
}

function __imvuFindSampleUserModel() {
    const roots = [];
    try { roots.push(window.__imvuCompanionActiveChat); } catch (e) {}
    try {
        const sv = window.__imvuServices;
        if (sv) for (const v of Object.values(sv)) roots.push(v);
    } catch (e) {}
    const seen = new Set();
    const q = roots.filter(Boolean);
    let n = 0;
    while (q.length && n++ < 800) {
        const o = q.shift();
        if (!o) continue;
        try { if (seen.has(o)) continue; seen.add(o); } catch (e) { continue; }
        try {
            if (typeof o.get === 'function' && o.get('legacy_cid') != null && o.get('display_name'))
                return o;
        } catch (e) {}
        try {
            const keys = Object.keys(o).slice(0, 40);
            for (const k of keys) {
                try {
                    const v = o[k];
                    if (v && typeof v === 'object') q.push(v);
                } catch (e) {}
            }
        } catch (e) {}
        try { if (o.models && o.models.length) for (const m of o.models) q.push(m); } catch (e) {}
    }
    return null;
}

function __imvuMakeUserModel(cid, uri, name) {
    const attrs = {
        id: uri,
        display_name: name || '',
        displayName: name || '',
        name: name || '',
        legacy_cid: cid,
        cid: cid
    };
    const sample = __imvuFindSampleUserModel();
    if (sample) {
        try {
            if (typeof sample.clone === 'function') {
                const c = sample.clone();
                if (c && typeof c.set === 'function') {
                    c.set(attrs);
                    try {
                        if (String(c.get('legacy_cid')) === String(cid))
                            return c;
                    } catch (e2) {}
                }
            }
        } catch (e) {}
        try {
            const Ctor = sample.constructor;
            if (typeof Ctor === 'function' && Ctor !== Object) {
                const m = new Ctor(attrs);
                try { if (m && typeof m.set === 'function') m.set(attrs); } catch (e2) {}
                if (m && typeof m.get === 'function') return m;
            }
        } catch (e) {}
    }
    const NM = window.IMVU && window.IMVU.NamedModel;
    if (typeof NM === 'function') {
        const envelopes = [
            attrs,
            { id: uri, data: { display_name: name || '', legacy_cid: cid } },
            { data: attrs }
        ];
        for (const env of envelopes) {
            try {
                const m = new NM(env);
                try { if (typeof m.set === 'function') m.set(attrs); } catch (e) {}
                if (m && typeof m.get === 'function') return m;
            } catch (e) {}
        }
    }
    return __imvuFakeUserModel(cid, 'user-' + cid, uri, name);
}

function __imvuFakeUserModel(cid, userId, uri, name) {
    const data = {
        display_name: name || '',
        displayName: name || '',
        name: name || '',
        legacy_cid: cid,
        cid: cid,
        id: uri
    };
    const attrs = {
        id: uri,
        cid: cid,
        legacy_cid: cid,
        url: uri,
        href: uri,
        rel: 'user',
        display_name: name || '',
        displayName: name || '',
        name: name || '',
        data: data
    };
    return {
        cid: cid,
        id: uri,
        url: uri,
        rel: 'user',
        data: data,
        get: function (k) {
            if (k === 'data') return data;
            if (Object.prototype.hasOwnProperty.call(attrs, k)) return attrs[k];
            if (Object.prototype.hasOwnProperty.call(data, k)) return data[k];
            return undefined;
        },
        set: function (k, v) {
            if (k && typeof k === 'object') {
                for (const x in k) attrs[x] = k[x];
                return this;
            }
            attrs[k] = v;
            return this;
        },
        toJSON: function () { return attrs; },
        toString: function () { return uri; }
    };
}

function __imvuSynthClick(el) {
    if (!el) return;
    const opts = { bubbles: true, cancelable: true, view: window, buttons: 1 };
    try { el.dispatchEvent(new PointerEvent('pointerdown', opts)); } catch (e) {}
    try { el.dispatchEvent(new MouseEvent('mousedown', opts)); } catch (e) {}
    try { el.dispatchEvent(new PointerEvent('pointerup', opts)); } catch (e) {}
    try { el.dispatchEvent(new MouseEvent('mouseup', opts)); } catch (e) {}
    try { el.dispatchEvent(new MouseEvent('click', opts)); } catch (e) {}
    try {
        const $ = window.jQuery || window.$;
        if ($) $(el).trigger('click');
    } catch (e) {}
}

function __imvuRestoreNewestDockWindow(userId) {
    const nodes = [];
    try {
        document.querySelectorAll('.window-manager .window, .window-manager .window-wrapper, .window.window-pulse')
            .forEach(function (el) { nodes.push(el); });
    } catch (e) {}
    let best = null;
    for (const el of nodes) {
        const mark = ((el.getAttribute && el.getAttribute('data-id')) || '') + (el.innerText || '');
        if (userId && mark.indexOf(userId) >= 0) best = el;
    }
    if (!best) best = nodes.length ? nodes[nodes.length - 1] : null;
    if (!best) return 'no-dock';

    const $ = window.jQuery || window.$;
    let view = null;
    try {
        if ($ && $._data && $.hasData && $.hasData(best)) {
            const raw = $._data(best);
            if (raw) {
                for (const v of Object.values(raw)) {
                    if (v && typeof v === 'object' &&
                        (typeof v.restore === 'function' || typeof v.set === 'function' ||
                         typeof v.maximize === 'function' || typeof v.trigger === 'function'))
                        view = v;
                }
            }
        }
    } catch (e) {}

    const names = ['restore', 'maximize', 'expand', 'show', 'open', 'unminimize', 'toggleMinimized', 'focus'];
    if (view) {
        for (const n of names) {
            try {
                if (typeof view[n] === 'function') {
                    view[n]();
                    return n;
                }
            } catch (e) {}
        }
        try {
            if (typeof view.trigger === 'function') {
                view.trigger('click');
                view.trigger('restore');
                return 'view-trigger';
            }
        } catch (e) {}
        try {
            if (typeof view.set === 'function') {
                view.set('minimized', false);
                view.set({ minimized: false, collapsed: false, hidden: false });
                return 'set-min-false';
            }
        } catch (e) {}
    }
    try {
        best.classList.remove('animate-up', 'window-pulse', 'minimized');
        const title = best.querySelector('.title, .window-title, .header, [class*="title"]') || best;
        __imvuSynthClick(title);
        __imvuSynthClick(best);
        return 'synth-click';
    } catch (e) {
        return 'restore-fail:' + (e && e.message ? e.message : e);
    }
}

function __imvuOpenNativeDmInner(uid, displayName) {
    const id = String(uid || '').replace(/^user-/i, '').replace(/\D/g, '');
    if (!id) return 'no-uid';
    const uri = 'https://api.imvu.com/user/user-' + id;
    const cid = Number(id);
    const userId = 'user-' + id;
    const name = String(displayName || '');
    const live = __imvuLiveTargetUser(cid);
    const targetUser = live || __imvuMakeUserModel(cid, uri, name);
    let windowModel = null;
    try {
        const NM = window.IMVU && window.IMVU.NamedModel;
        if (typeof NM === 'function')
            windowModel = new NM({ id: uri });
    } catch (e) {}

    const found = { messages: null, select: null, addWindow: null };
    function consider(o) {
        if (!o || (typeof o !== 'object' && typeof o !== 'function')) return;
        try {
            if (typeof o.addMessagesWindow === 'function') found.messages = o;
            if (typeof o.__selectConversation === 'function') found.select = o;
            if (typeof o.addWindow === 'function') found.addWindow = o;
        } catch (e) {}
        try { if (o.prototype) consider(o.prototype); } catch (e) {}
    }

    function walk(root, max) {
        if (!root) return;
        const seen = new Set();
        const q = [root];
        let n = 0;
        while (q.length && n++ < max && !found.messages) {
            const o = q.shift();
            if (!o) continue;
            try {
                if (seen.has(o)) continue;
                seen.add(o);
            } catch (e) { continue; }
            consider(o);
            let keys;
            try { keys = Object.keys(o); } catch (e) { continue; }
            if (keys.length > 50) keys = keys.slice(0, 50);
            for (const k of keys) {
                try {
                    const v = o[k];
                    if (v && (typeof v === 'object' || typeof v === 'function'))
                        q.push(v);
                } catch (e) {}
            }
        }
    }

    const wins = [window];
    try { if (window.top && window.top !== window) wins.push(window.top); } catch (e) {}

    let serviceNames = [];
    for (const w of wins) {
        try { consider(w.IMVU); } catch (e) {}
        try {
            const sv = w.__imvuServices;
            if (sv) {
                serviceNames = serviceNames.concat(Object.keys(sv));
                for (const v of Object.values(sv)) walk(v, 400);
            }
        } catch (e) {}
        try { walk(w.IMVU && w.IMVU.serviceProvider, 400); } catch (e) {}
        try { walk(w.__imvuCompanionActiveChat, 200); } catch (e) {}
        try {
            const $ = w.jQuery || w.$;
            const roots = w.document.querySelectorAll('.window-manager, [class*="withme"], [class*="with-me"]');
            for (const el of roots) {
                consider(el);
                if ($) {
                    try {
                        const raw = ($.hasData && $.hasData(el) && $._data) ? $._data(el) : $(el).data();
                        if (raw) {
                            consider(raw);
                            walk(raw, 80);
                            for (const v of Object.values(raw)) consider(v);
                        }
                    } catch (e) {}
                }
            }
        } catch (e) {}
    }

    const host = found.messages || found.select || found.addWindow;
    if (!host) {
        let keys = '';
        try { keys = Object.keys(window.IMVU || {}).slice(0, 16).join(','); } catch (e) {}
        return 'no-host services=[' + serviceNames.slice(0, 30).join(',') + '] IMVU=[' + keys + ']';
    }

    let how = '';
    let lastErr = '';
    let created = null;
    function tryCall(label, fn) {
        try {
            const r = fn();
            created = r;
            if (r && typeof r.then === 'function') {
                r.then(function (v) { created = v; }, function () {});
            }
            how = label;
            return true;
        } catch (e) {
            lastErr = (e && e.message) ? e.message : String(e);
            return false;
        }
    }

    const awHost = found.messages || found.addWindow;
    if (awHost && typeof awHost.addWindow === 'function' && !awHost.addWindow.__imvuSteal) {
        const origAw = awHost.addWindow.bind(awHost);
        awHost.addWindow = function () {
            const r = origAw.apply(awHost, arguments);
            try { window.__imvuLastMessagesView = r; } catch (e) {}
            return r;
        };
        awHost.addWindow.__imvuSteal = true;
    }

    const opts = { targetUser: targetUser };
    if (windowModel) opts.model = windowModel;

    if (found.messages)
        tryCall('profile', function () { return found.messages.addMessagesWindow(uri, opts); });
    else if (found.addWindow)
        tryCall('addWindow-profile', function () { return found.addWindow.addWindow(uri, null, opts); });
    if (!how)
        return 'call-failed:' + lastErr;

    if (!created)
        try { created = window.__imvuLastMessagesView; } catch (e) {}

    let viewAct = 'none';
    if (created && typeof created === 'object') {
        const names = ['restore', 'maximize', 'expand', 'show', 'open', 'unminimize', 'focus', 'render'];
        for (const n of names) {
            try {
                if (typeof created[n] === 'function') {
                    created[n]();
                    viewAct = n;
                    break;
                }
            } catch (e) {}
        }
        if (viewAct === 'none') {
            try {
                if (typeof created.set === 'function') {
                    created.set('minimized', false);
                    created.set({ minimized: false, collapsed: false });
                    viewAct = 'set';
                }
            } catch (e) {}
        }
        if (viewAct === 'none') {
            try {
                const fns = [];
                for (const k in created) {
                    try {
                        if (typeof created[k] === 'function' &&
                            /restore|max|expand|show|open|minimi|render|focus|toggle/i.test(k))
                            fns.push(k);
                    } catch (e) {}
                }
                viewAct = 'fns=' + fns.slice(0, 12).join(',');
            } catch (e) { viewAct = 'no-fns'; }
        }
    }

    let dn = '', cidok = '';
    try { dn = String(targetUser.get && targetUser.get('display_name') || ''); } catch (e) {}
    try { cidok = String(targetUser.get && targetUser.get('legacy_cid') || ''); } catch (e) {}
    return 'ok:' + how + ':live=' + (!!live) + ':dn=' + dn + ':cid=' + cidok + ':view=' + viewAct;
}

function __imvuFindRequester() {
    const found = { strong: null, weak: null };
    function consider(o) {
        if (!o) return;
        try {
            if (typeof o.__request === 'function' &&
                typeof o.__makeRequest === 'function' &&
                typeof o.__queueRequest === 'function')
                found.strong = o;
            else if (typeof o.__request === 'function')
                found.weak = found.weak || o;
        } catch (e) {}
    }
    function walk(root, max) {
        if (!root) return;
        const seen = new Set();
        const q = [root];
        let n = 0;
        while (q.length && n++ < max && !found.strong) {
            const o = q.shift();
            if (!o) continue;
            try { if (seen.has(o)) continue; seen.add(o); } catch (e) { continue; }
            consider(o);
            let keys;
            try { keys = Object.keys(o); } catch (e) { continue; }
            for (const k of keys.slice(0, 40)) {
                try {
                    const v = o[k];
                    if (v && typeof v === 'object') q.push(v);
                } catch (e) {}
            }
        }
    }
    try { walk(window.__imvuServices, 500); } catch (e) {}
    try {
        const sv = window.__imvuServices;
        if (sv) for (const v of Object.values(sv)) walk(v, 400);
    } catch (e) {}
    try { walk(window.IMVU, 500); } catch (e) {}
    try { walk(window.__imvuCompanionActiveChat, 200); } catch (e) {}
    return found.strong || found.weak;
}

function __imvuGuessSelfCid() {
    try {
        const entries = performance.getEntriesByType('resource');
        for (let i = entries.length - 1; i >= 0; i--) {
            const m = String(entries[i].name).match(/\/user\/user-(\d+)\/conversations/);
            if (m) return m[1];
        }
    } catch (e) {}
    return '';
}

function __imvuIsConvUrl(u) {
    return typeof u === 'string' && /https:\/\/api\.imvu\.com\/conversation\/conversation-\d+$/.test(u);
}

function __imvuConversationIdFrom(resp) {
    if (resp == null) return '';
    function pick(s) {
        const all = String(s).match(/https:\/\/api\.imvu\.com\/conversation\/conversation-\d+/g) || [];
        for (const u of all) {
            if (__imvuIsConvUrl(u)) return u;
        }
        return '';
    }
    if (typeof resp === 'string') return pick(resp);
    try { if (__imvuIsConvUrl(resp.id)) return resp.id; } catch (e) {}
    try {
        const den = resp.denormalized || {};
        for (const k of Object.keys(den)) {
            if (__imvuIsConvUrl(k)) return k;
            const node = den[k];
            if (!node) continue;
            if (__imvuIsConvUrl(node.id)) return node.id;
            const data = node.data || {};
            if (__imvuIsConvUrl(data.id)) return data.id;
            const items = data.items || data.conversations || data.refs;
            if (Array.isArray(items)) {
                for (const it of items) {
                    if (typeof it === 'string' && __imvuIsConvUrl(it)) return it;
                    if (it && __imvuIsConvUrl(it.id)) return it.id;
                }
            }
        }
    } catch (e) {}
    try { return pick(JSON.stringify(resp)); } catch (e) {}
    return '';
}

function __imvuSauce() {
    try {
        const c = window.IMVU && typeof window.IMVU.getCookies === 'function' && window.IMVU.getCookies();
        if (c && (c.sauce || c.SAUCE)) return c.sauce || c.SAUCE;
    } catch (e) {}
    try {
        const m = String(document.cookie || '').match(/(?:^|;\s*)sauce=([^;]+)/i);
        if (m) return decodeURIComponent(m[1]);
    } catch (e) {}
    return '';
}

function __imvuHttp(method, url, data) {
    return new Promise(function (resolve, reject) {
        const xhr = new XMLHttpRequest();
        xhr.open(method, url, true);
        try { xhr.withCredentials = true; } catch (e) {}
        try { xhr.setRequestHeader('Accept', 'application/json'); } catch (e) {}
        if (data !== undefined) {
            try { xhr.setRequestHeader('Content-Type', 'application/json'); } catch (e) {}
        }
        const sauce = __imvuSauce();
        if (sauce) {
            try { xhr.setRequestHeader('X-IMVU-Sauce', sauce); } catch (e) {}
        }
        xhr.onload = function () {
            const text = xhr.responseText || '';
            let json = null;
            try { json = JSON.parse(text); } catch (e) {}
            if (xhr.status < 200 || xhr.status >= 300) {
                reject(new Error(method + ':' + xhr.status + ':' + text.slice(0, 80)));
                return;
            }
            resolve(json != null ? json : text);
        };
        xhr.onerror = function () { reject(new Error(method + ':network')); };
        xhr.send(data !== undefined ? JSON.stringify(data) : null);
    });
}

function __imvuExtractPayloads(resp) {
    try {
        const den = resp && resp.denormalized;
        if (!den) return null;
        for (const k of Object.keys(den)) {
            if (k.indexOf('/message/') < 0 && k.indexOf('/messages/') < 0) continue;
            const data = den[k] && den[k].data;
            if (data && data.payloads) return data.payloads;
        }
    } catch (e) {}
    return null;
}

function __imvuPayloadsForText(text, template) {
    if (template && typeof template === 'object') {
        try {
            const copy = JSON.parse(JSON.stringify(template));
            if (Array.isArray(copy)) {
                if (copy[0] && typeof copy[0] === 'object') {
                    if ('content' in copy[0]) copy[0].content = text;
                    else if ('text' in copy[0]) copy[0].text = text;
                    else copy[0] = { type: 'text', content: text };
                }
                return copy;
            }
            const first = copy['0'] || copy[0];
            if (first && typeof first === 'object') {
                if ('content' in first) first.content = text;
                else if ('text' in first) first.text = text;
                copy['0'] = first;
                return copy;
            }
        } catch (e) {}
    }
    return { '0': { type: 'text', content: text } };
}

function __imvuSendNativeDm(targetCid, text, selfCid) {
    const to = String(targetCid || '').replace(/\D/g, '');
    const body = String(text || '');
    if (!to) return Promise.resolve('no-uid');
    if (!body.trim()) return Promise.resolve('no-text');
    const self = String(selfCid || '').replace(/\D/g, '') || __imvuGuessSelfCid();
    if (!self) return Promise.resolve('no-self-cid');
    if (self === to) return Promise.resolve('self-target');
    const selfUri = 'https://api.imvu.com/user/user-' + self;
    const toUri = 'https://api.imvu.com/user/user-' + to;
    const lookup = 'https://api.imvu.com/conversation?participants=' + selfUri + ',' + toUri;
    return __imvuHttp('GET', lookup).then(function (resp) {
        const conv = __imvuConversationIdFrom(resp);
        if (!conv)
            return 'err:GET:no-conversation';
        const msgUrl = conv.replace(/\/$/, '') + '/messages';
        const short = conv.replace(/^https:\/\/api\.imvu\.com\/conversation\//, '');
        const payload = { payloads: [{ type: 'text', content: body }] };
        const client = __imvuFindRequester();
        if (!client || typeof client.__makeRequest !== 'function')
            return 'err:POST:no-requester:to=' + short;
        return new Promise(function (resolve) {
            let settled = false;
            function done(r) {
                if (settled) return;
                settled = true;
                resolve(r);
            }
            const opts = {
                validate: true,
                parse: true,
                data: payload,
                success: function () { done('ok:' + short); },
                error: function (e) {
                    const t = (e && e.message) ? e.message : String(e || 'error');
                    done('err:POST:make:' + t.slice(0, 80) + ':to=' + short);
                }
            };
            try {
                client.__makeRequest('POST', msgUrl, payload, opts, function (err) {
                    if (err) {
                        const t = (err && err.message) ? err.message : String(err);
                        done('err:POST:cb:' + t.slice(0, 80) + ':to=' + short);
                    } else
                        done('ok:' + short);
                });
            } catch (e) {
                done('err:POST:throw:' + (e && e.message ? e.message : e));
                return;
            }
            setTimeout(function () {
                if (settled) return;
                __imvuHttp('GET', msgUrl).then(function (list) {
                    const s = JSON.stringify(list || '');
                    done(s.indexOf(body) >= 0
                        ? 'ok:' + short
                        : 'err:POST:timeout-not-in-thread:to=' + short);
                }).catch(function () {
                    done('err:POST:timeout:to=' + short);
                });
            }, 5000);
        });
    }).catch(function (err) {
        return 'err:' + (err && err.message ? err.message : String(err)).slice(0, 160);
    });
}
