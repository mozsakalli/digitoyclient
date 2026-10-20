// Unity GameProperties'in ortak ağ parçası (01-architecture §3): ClientBoot içinde GÖMÜLÜ (oyun başına Boot.asset), ayrı dosya değil.
// ORTAM SEÇİMİ DOSYADA DEĞİL BUILD TÜRÜNDE (game_rd docs "Dev build"): release pak'ında yalnız prod URL var ([DevOnly] alanlar pak'a
// yazılmaz), GameHost.IsDev=false → her zaman prod. Dev build / editör Play: PlayerPrefs'teki seçim (DevEnv: test|prod|custom) → yoksa test.
// Dev override'lar (custom URL, device key) PlayerPrefs'te; release'te okunmaz bile.
using System;
using DigitoyEngine;

namespace Digitoy
{
    [Serializable]
    public class NetworkSettings
    {
        public string prodServerUrl = "https://api.digitoy.co";
        [DevOnly] public string testServerUrl = "https://test-node.digitoy.co";
        /// <summary>Dev: dolu ise cihaz anahtarı yerine bu gönderilir (başka test hesabıyla girmek için; Boot.asset'te durur, pak'a yazılmaz).
        /// PlayerPrefs override'ı (DevEnvSwitch) doluysa o öncelikli.</summary>
        [DevOnly] public string devDeviceKey = "";

        public const string EnvTest = "test", EnvProd = "prod", EnvCustom = "custom";
        private const string EnvPref = "__dev_env__";
        private const string CustomUrlPref = "__dev_server_url__";

        /// <summary>Dev build'de seçili ortam (test|prod|custom; varsayılan test). Release'te her zaman prod.</summary>
        public static string DevEnv
        {
            get => GameHost.IsDev ? PlayerPrefs.GetString(EnvPref, EnvTest) : EnvProd;
            set { PlayerPrefs.SetString(EnvPref, value ?? EnvTest); PlayerPrefs.Save(); }
        }

        /// <summary>Yerel/dev override: doluysa settings de WS de doğrudan bu adrese, gateway/seed listesi yoksayılır.</summary>
        public string CustomServerUrl => GameHost.IsDev ? PlayerPrefs.GetString(CustomUrlPref, "") : "";
        public bool HasCustomServerUrl => GameHost.IsDev && DevEnv == EnvCustom && !string.IsNullOrWhiteSpace(CustomServerUrl);
        /// <summary>Dev: Boot.asset'teki devDeviceKey (boşsa override yok). Release'te hep boş.</summary>
        public string OverrideDeviceKey => GameHost.IsDev ? (devDeviceKey ?? "").Trim() : "";
        public static void SetCustomServerUrl(string url) { PlayerPrefs.SetString(CustomUrlPref, url ?? ""); PlayerPrefs.Save(); }

        public bool IsTest => GameHost.IsDev && DevEnv == EnvTest;

        public string RemoteUrl =>
            HasCustomServerUrl ? CustomServerUrl.Trim().TrimEnd('/')
            : (IsTest && !string.IsNullOrWhiteSpace(testServerUrl) ? testServerUrl : prodServerUrl).Trim().TrimEnd('/');

        /// <summary>Şemasız host (settings cevabı da yalnız host döner).</summary>
        public string BaseHost
        {
            get
            {
                var url = RemoteUrl;
                var i = url.IndexOf("://", StringComparison.Ordinal);
                if (i >= 0) url = url[(i + 3)..];
                return url.TrimEnd('/');
            }
        }

        public string WebSocketUrl => ToWebSocketUrl(BaseHost);

        /// <summary>Host → WS adresi. Tarayıcı (wasm) dışında her yerde <c>ws://</c>; tarayıcı mixed-content için <c>wss://</c>.</summary>
        public static string ToWebSocketUrl(string host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;
            var scheme = OperatingSystem.IsBrowser() ? "wss" : "ws";
            return $"{scheme}://{host}/api/websocket/connect";
        }
    }
}
