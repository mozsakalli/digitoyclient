// core.upm DeviceKey/{DeviceKey,PersistentDeviceKey,Helpers/DeviceKeyHelper}.cs adaptasyonu (Faz 0 = masaüstü/editör).
// Fark: SystemInfo.deviceUniqueIdentifier yerine PlayerPrefs'te saklanan GUID (yeniden kurulumda değişir — Faz 5'te
// Android ANDROID_ID/Widevine ve iOS KeyChain native köprüyle gelir). Eski Java istemci göç yolu (AES storage,
// SharedPreferences) Android'e özgü olduğu için alınmadı; `_pk_` saklama düz metin (AES corelib'de yok, yalnız yerel okunur).
using System;
using System.Security.Cryptography;
using System.Text;
using DigitoyEngine;

namespace Digitoy
{
    internal static class DeviceKey
    {
        private const string StorageKey = "__dk_guid__";
        private static string _value;

        /// <summary>Donanım kimliği karşılığı: ilk çalışmada üretilen, PlayerPrefs'te saklanan GUID.</summary>
        public static string Value
        {
            get
            {
                if (_value != null) return _value;
                _value = PlayerPrefs.GetString(StorageKey, null);
                if (string.IsNullOrEmpty(_value))
                {
                    _value = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(StorageKey, _value);
                    PlayerPrefs.Save();
                }
                return _value;
            }
        }

        internal static string CRC(string src)
        {
            var crc = src.Length;
            for (var i = 0; i < src.Length; i++)
                crc += src[i];
            return src + "-" + (crc & 0xff).ToString("X");
        }
    }

    /// <summary>Kalıcı cihaz anahtarı: HMAC-SHA256(rawId, secret) GUID biçiminde.</summary>
    internal static class PersistentDeviceKey
    {
        // DEĞİŞTİRME — mevcut anahtarları bozar.
        private static readonly byte[] _k = { 0x64, 0x39, 0x74, 0x6F, 0x79, 0x5F, 0x70, 0x64, 0x6B, 0x5F, 0x73, 0x31, 0x67, 0x6E };

        private static string _value;

        public static string Value => _value ??= DeriveKey(DeviceKey.Value);

        // Faz 5: Android "widevine"/"android_id", iOS "keychain".
        public static string Method => "prefs_guid";

        internal static string DeriveKey(string rawId)
        {
            using var hmac = new HMACSHA256(_k);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawId));
            var guidBytes = new byte[16];
            Buffer.BlockCopy(hash, 0, guidBytes, 0, 16);
            return new Guid(guidBytes).ToString("D").ToUpperInvariant();
        }
    }

    internal static class DeviceKeyHelper
    {
        // Unity'de AES ile gizlenmiş saklanıyordu; AOT corelib'de Aes yok ve değer yalnız yerel okunur → düz "key:method".
        private const string PERSISTENT_STORAGE_KEY = "_pk_";

        public static string GetDeviceKey() => DeviceKey.Value;
        public static string GetPersistentDeviceKey() => PersistentDeviceKey.Value;
        public static string GetPersistentDeviceKeyMethod() => PersistentDeviceKey.Method;

        public static void SavePersistentDeviceKey(string key, string method)
        {
            PlayerPrefs.SetString(PERSISTENT_STORAGE_KEY, key + ":" + method);
        }

        public static void GetPersistentDeviceKeyFromStorage(out string key, out string method)
        {
            key = null;
            method = null;
            var stored = PlayerPrefs.GetString(PERSISTENT_STORAGE_KEY, null);
            if (string.IsNullOrEmpty(stored)) return;
            var parts = stored.Split(':', 2);
            if (parts.Length != 2) return;
            key = parts[0];
            method = parts[1];
        }
    }
}
