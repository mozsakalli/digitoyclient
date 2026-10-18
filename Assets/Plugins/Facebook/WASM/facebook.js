// Facebook host plugin'i — WASM (docs/plugins.md). plugins.js'e birlestirilir (senkron, game.js'den once).
// FB JS SDK'si FacebookWasm kancasinin Script() satiriyla yuklenir; appId window.DEConfig.facebookAppId.
DE.register({
  name: 'Facebook',
  onMessage: function (msg) {
    var j = JSON.parse(msg), self = this;
    if (j.method === 'login') {
      if (typeof FB === 'undefined') { DE.send('Facebook', JSON.stringify({ id: j.id, ok: false, error: 'sdk_not_loaded' })); return; }
      if (!this.inited) { FB.init({ appId: DEConfig.facebookAppId, version: 'v19.0' }); this.inited = true; }
      FB.login(function (r) {
        var ok = r && r.authResponse;
        DE.send('Facebook', JSON.stringify(ok ? { id: j.id, ok: true, token: r.authResponse.accessToken, userId: r.authResponse.userID }
                                             : { id: j.id, ok: false, error: 'cancel' }));
      }, { scope: (j.permissions || ['public_profile']).join(',') });
    } else if (j.method === 'logout' && typeof FB !== 'undefined') FB.logout();
  }
});
