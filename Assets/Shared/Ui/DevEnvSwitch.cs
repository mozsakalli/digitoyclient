// Dev build'de ortam secici (game_rd docs "Dev build"): surum etiketine 5 hizli tik → test → prod → custom → test dongusu
// (NetworkSettings.DevEnv, PlayerPrefs). Release'te GameHost.IsDev=false: hicbir sey yapmaz (test URL pak'ta zaten yok).
// Gorunum sahnede (LayoutBox); bu component ayni GO'ya eklenir. Degisiklik sonrasi uygulama yeniden baglanmali: LoginFlow
// etiketi tazeler ve sayfayi yeniden yukler (ClearSession + ProgressAsync).
using System;
using DigitoyEngine;

namespace Digitoy.Ui
{
    public sealed class DevEnvSwitch : Component
    {
        public int tapsToToggle = 5;
        public float tapWindowSeconds = 2f;

        public event Action<string> EnvChanged;

        private int _taps;
        private float _firstTapTime;

        protected override void OnPointerClick(PointerEvent e)
        {
            if (!GameHost.IsDev) return;
            float now = Time.time;
            if (_taps == 0 || now - _firstTapTime > tapWindowSeconds) { _taps = 0; _firstTapTime = now; }
            if (++_taps < tapsToToggle) return;
            _taps = 0;
            string next = NetworkSettings.DevEnv switch
            {
                NetworkSettings.EnvTest => NetworkSettings.EnvProd,
                NetworkSettings.EnvProd => string.IsNullOrWhiteSpace(World.Net?.CustomServerUrl) ? NetworkSettings.EnvTest : NetworkSettings.EnvCustom,
                _ => NetworkSettings.EnvTest,
            };
            NetworkSettings.DevEnv = next;
            Debug.Log("[dev] env -> " + next + " (" + World.Net?.RemoteUrl + ")");
            EnvChanged?.Invoke(next);
        }

        /// <summary>Surum etiketi eki: dev build'de " · test" / " · prod" / " · custom"; release'te bos.</summary>
        public static string Suffix => GameHost.IsDev ? " \u00B7 " + NetworkSettings.DevEnv : "";
    }
}
