(function () {
  function installOn(w) {
    if (!w) return;
    try {
      if (w.__imvuCompanionHooksInstalled) {
        // still try to read already-registered activeChat
        try {
          for (const k of Object.getOwnPropertyNames(w)) {
            try {
              const v = w[k];
              if (v && typeof v.get === 'function') {
                const ac = v.get('activeChat');
                if (ac) {
                  w.__imvuCompanionActiveChat = ac;
                  try { if (w.top) w.top.__imvuCompanionActiveChat = ac; } catch (e) {}
                }
              }
            } catch (e) {}
          }
        } catch (e) {}
        return;
      }
      w.__imvuCompanionHooksInstalled = true;
    } catch (e) { return; }

    function capture(name, value) {
      if (name !== 'activeChat' || !value) return;
      try { w.__imvuCompanionActiveChat = value; } catch (e) {}
      try { if (w.top) w.top.__imvuCompanionActiveChat = value; } catch (e) {}
      try { if (w.parent && w.parent !== w) w.parent.__imvuCompanionActiveChat = value; } catch (e) {}
    }

    function hookRegisterFn(obj) {
      if (!obj || obj.__imvuCompanionRegHooked) return;
      const orig = obj.register;
      if (typeof orig !== 'function') return;
      obj.register = function (name, value) {
        try { capture(name, value); } catch (e) {}
        return orig.apply(this, arguments);
      };
      obj.__imvuCompanionRegHooked = true;
    }

    function scanAndHook() {
      try {
        if (w.IMVU) {
          if (w.IMVU.ServiceProvider && w.IMVU.ServiceProvider.prototype)
            hookRegisterFn(w.IMVU.ServiceProvider.prototype);
          if (w.IMVU.serviceProvider) hookRegisterFn(w.IMVU.serviceProvider);
        }
      } catch (e) {}
      try {
        for (const k of Object.getOwnPropertyNames(w)) {
          try {
            const v = w[k];
            if (!v || typeof v !== 'object') continue;
            if (typeof v.register === 'function' && typeof v.get === 'function') {
              hookRegisterFn(v);
              try {
                const ac = v.get('activeChat');
                if (ac) capture('activeChat', ac);
              } catch (e) {}
            }
            if (v.prototype && typeof v.prototype.register === 'function')
              hookRegisterFn(v.prototype);
          } catch (e) {}
        }
      } catch (e) {}
    }

    scanAndHook();
    let n = 0;
    const t = w.setInterval(function () {
      scanAndHook();
      if (++n > 180) w.clearInterval(t);
    }, 500);
  }

  try {
    window.__imvuCompanionInstallHooks = installOn;
    installOn(window);
    // same-origin chat iframes
    try {
      for (const f of document.querySelectorAll('iframe')) {
        try { installOn(f.contentWindow); } catch (e) {}
      }
    } catch (e) {}
  } catch (e) {}
})();
