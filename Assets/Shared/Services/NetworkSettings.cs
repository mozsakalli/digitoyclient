// Unity GameProperties'in ortak ağ parçası (01-architecture §3): tüm oyunlar için tek asset. Dev override'lar
// (customServerUrl, overrideDeviceKey) asset'e yazılmaz; PlayerPrefs'ten okunur (dev panel Faz 1).
using System;
using DigitoyEngine;

namespace Digitoy
{
    [Serializable]
    [CreateAssetMenu(MenuName = "Digitoy/Network Settings", FileName = "Settings")]
    public class NetworkSettings
    {
        public string testServerUrl = "https://test-node.digitoy.co";
        public string prodServerUrl = "https://api.digitoy.co";
        public bool useTestServer = true;
        public int connectionTimeoutSeconds = 10;
        public int requestTimeoutSeconds = 30;

        private const string CustomUrlPref = "__dev_server_url__";
        private const string OverrideDeviceKeyPref = "__dev_device_key__";

        /// <summary>Yerel/dev override: doluysa settings de WS de doğrudan bu adrese, gateway/seed listesi yoksayılır.</summary>
        public string CustomServerUrl => PlayerPrefs.GetString(CustomUrlPref, "");
        public bool HasCustomServerUrl => !string.IsNullOrWhiteSpace(CustomServerUrl);
        public string OverrideDeviceKey => PlayerPrefs.GetString(OverrideDeviceKeyPref, "");

        public string RemoteUrl =>
            HasCustomServerUrl ? CustomServerUrl.Trim().TrimEnd('/')
            : (useTestServer ? testServerUrl : prodServerUrl).Trim().TrimEnd('/');

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

        public static string ToWebSocketUrl(string host)
        {
            if (string.IsNullOrEmpty(host)) return string.Empty;
            var scheme = IsLocalHost(host) ? "ws" : "wss";
            return $"{scheme}://{host}/api/websocket/connect";
        }

        private static bool IsLocalHost(string host) =>
            host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("192.168.", StringComparison.OrdinalIgnoreCase);
    }
}
