// core.upm Network/ServerDirectory.cs adaptasyonu. Fark: GameProperties → NetworkSettings; JsonUtility string listesi
// → '|' ile birleştirilmiş PlayerPrefs string'i (host'ta '|' olmaz). Davranış aynı.
#nullable enable
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace Digitoy
{
    /// <summary>
    /// Sunucu adres defteri. Kaynak: <c>GET /api/config/{g}/settings</c> cevabındaki <c>gateways</c> (canlı gateway HOST'ları, şema/yol yok; sunucu
    /// yük ağırlıklı karıştırır) ve <c>seeds</c> (bir sonraki açılışta önce denenecek bootstrap adresleri). Gateway'ler sırayla
    /// denenir; soket hiç açılamayan atlanır, liste tükenince <see cref="NeedsRefresh"/> → settings yeniden çekilir.
    /// <c>customServerUrl</c> (yerel/dev) doluysa defter devre dışı: her şey doğrudan o adrese.
    /// </summary>
    public static class ServerDirectory
    {
        private const string GatewaysKey = "__gateways__";
        private const string SeedsKey = "__seeds__";
        private const char Sep = '|';

        // List<string> (dizi degil): AOT'de T[] -> IReadOnlyList<T> arayuz dispatch'i yok (dizi descriptor'u itable tasimaz,
        // docs/engine-notes.md "Dizi arayüz haritası HENÜZ YOK"); Gateways.Count AOT'de InvalidCast veriyordu.
        private static List<string> _gateways = new List<string>();
        private static bool _loaded;
        private static int _index;
        private static bool _exhausted;

        public static IReadOnlyList<string> Gateways
        {
            get { EnsureLoaded(); return _gateways; }
        }

        /// <summary>Liste yok ya da hepsi denenip tükendi: bağlanmadan önce settings tazelenmeli.</summary>
        public static bool NeedsRefresh
        {
            get { EnsureLoaded(); return _exhausted || _gateways.Count == 0; }
        }

        /// <summary>Settings cevabı geldi. Boş liste = değişme (sunucu sözleşmesi).</summary>
        public static void Apply(string[]? gateways, string[]? seeds)
        {
            EnsureLoaded();
            var g = Clean(gateways);
            if (g.Length > 0)
            {
                _gateways = new List<string>(g);
                _index = 0;
                _exhausted = false;
                Save(GatewaysKey, g);
            }
            var s = Clean(seeds);
            if (s.Length > 0) Save(SeedsKey, s);
        }

        /// <summary>Bootstrap (settings) adresleri, deneme sırasıyla: saklı seed'ler, sonra gömülü adres. customServerUrl varsa yalnız o.</summary>
        public static List<string> BootstrapUrls(NetworkSettings net)
        {
            var list = new List<string>();
            if (net.HasCustomServerUrl)
            {
                list.Add(net.RemoteUrl);
                return list;
            }
            foreach (var s in Load(SeedsKey)) AddUnique(list, s);
            AddUnique(list, net.RemoteUrl);
            return list;
        }

        /// <summary>Şimdi denenecek WS adresi: customServerUrl → doğrudan; liste varsa sıradaki host; yoksa/tükendiyse gömülü host.</summary>
        public static string WebSocketUrl(NetworkSettings net)
        {
            if (net.HasCustomServerUrl) return net.WebSocketUrl;
            EnsureLoaded();
            if (_gateways.Count == 0 || _index >= _gateways.Count) return net.WebSocketUrl;
            return ToWebSocketUrl(_gateways[_index]);
        }

        // Sunucu yalnız host döner (şema/yol yok); şema ileride platforma göre seçilecek, şimdilik ws
        private static string ToWebSocketUrl(string host) => $"ws://{host}/api/websocket/connect";

        /// <summary>Soket bu adrese hiç açılamadı → sıradakine geç. true = denenecek başka gateway var.</summary>
        public static bool MarkUnreachable(string url)
        {
            EnsureLoaded();
            if (_gateways.Count == 0 || _index >= _gateways.Count || ToWebSocketUrl(_gateways[_index]) != url) return false;
            _index++;
            if (_index < _gateways.Count) return true;
            _exhausted = true;
            return false;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _gateways = new List<string>(Load(GatewaysKey));
            _index = 0;
            _exhausted = false;
        }

        private static string[] Clean(string[]? src)
        {
            if (src == null || src.Length == 0) return Array.Empty<string>();
            var list = new List<string>(src.Length);
            foreach (var s in src) AddUnique(list, s);
            return list.ToArray();
        }

        private static void AddUnique(List<string> list, string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            var u = url.Trim().TrimEnd('/');
            if (!list.Contains(u)) list.Add(u);
        }

        private static string[] Load(string key)
        {
            var s = PlayerPrefs.GetString(key, "");
            return string.IsNullOrEmpty(s) ? Array.Empty<string>() : s.Split(Sep, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void Save(string key, string[] items)
        {
            PlayerPrefs.SetString(key, string.Join(Sep, items));
            PlayerPrefs.Save();
        }
    }
}
