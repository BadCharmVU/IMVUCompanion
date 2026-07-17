module({
    hb: "hb",
}, function ($module$deferred) {
    /* src/ServiceProvider.real.js */
    var $module$1 = function () {
    return IMVU.ServiceProvider.extend('ServiceProvider', {
        initialize: function () {
            IMVU.ServiceProvider.prototype.initialize.call(this);
            this.register('random', new IMVU.Random());
            this.register('timer', IMVU.Timer);
            var PromiseFactory = IMVU.PromiseFactory;
            if (IMVU.NativePromiseFactory) {
                window.console.log('NativePromiseFactory', IMVU.NativePromiseFactory);
                PromiseFactory = IMVU.NativePromiseFactory;
            }
            this.register('Promise', new PromiseFactory(IMVU.EventLoop, {
                immediateCallbacks: true,
                exposeErrors: true,
            }));
            this.register('XMLHttpRequest', IMVU.XMLHttpRequest);
        },
    });
}
    ({
        // No arguments are passed.
    });

    return $module$1;
});
//# sourceMappingURL=ServiceProvider.real.js.map