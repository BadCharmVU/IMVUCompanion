(function () {
  // Light hook: only wrap IMVU.ServiceProvider.register. No window-property scan
  // (that competed with IMVU boot and can look like a bot).
  function installOn(w) {
    if (!w) return;
    try {
      if (w.__imvuCompanionHooksInstalled) return;
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

    function tryHookImvu() {
      try {
        const I = w.IMVU;
        if (!I) return false;
        if (I.ServiceProvider && I.ServiceProvider.prototype)
          hookRegisterFn(I.ServiceProvider.prototype);
        if (I.serviceProvider) hookRegisterFn(I.serviceProvider);
        return true;
      } catch (e) {
        return false;
      }
    }

    if (tryHookImvu()) return;
    let n = 0;
    const t = w.setInterval(function () {
      if (tryHookImvu() || ++n > 40) w.clearInterval(t);
    }, 1000);
  }

  try {
    window.__imvuCompanionInstallHooks = installOn;
    installOn(window);
  } catch (e) {}
})();
