// core.upm Components/Config/GameSettings.cs adaptasyonu. Fark: UNITY_ANDROID/IOS → Platform.Current, Application.version →
// Platform.AppVersion. Sunucu JSON alan adları snake_case; Json.FromJson alan adıyla eşler, dokunma.
#nullable enable
using System;
using M;
using DigitoyEngine;

namespace Digitoy
{
    [Serializable]
    public class GameSettings
    {
        public string? ios_soft_version;
        public string? ios_force_version;
        public string? android_soft_version;
        public string? android_force_version;
        public bool maintenance;
        public string? privacy_url;
        public string? terms_url;
        public string? support_url;
        public string? delete_account_url;
        public int policy_version;

        private const string PrivacyVersionKey = "privacy_policy_date";

        private static int PrivacyVersion
        {
            get => PlayerPrefs.GetInt(PrivacyVersionKey, 0);
            set { PlayerPrefs.SetInt(PrivacyVersionKey, value); PlayerPrefs.Save(); }
        }

        /// <summary>Platformun zorunlu sürümü uygulama sürümünden büyükse true (masaüstü/web: hiç).</summary>
        public bool ShouldUpdate()
        {
            var minimumVersion = Platform.Current switch
            {
                DevicePlatform.Android => android_force_version,
                DevicePlatform.Ios => ios_force_version,
                _ => null
            };
            if (string.IsNullOrEmpty(minimumVersion)) return false;
            var p1 = Platform.AppVersion.Split('.');
            var p2 = minimumVersion.Split('.');
            var len = Math.Max(p1.Length, p2.Length);
            for (var i = 0; i < len; i++)
            {
                var a = i < p1.Length && int.TryParse(p1[i], out var aVal) ? aVal : 0;
                var b = i < p2.Length && int.TryParse(p2[i], out var bVal) ? bVal : 0;
                if (a != b) return a < b;
            }
            return false;
        }

        public bool ForceUpdate() => ShouldUpdate();

        /// <summary>Kullanıcı daha önce bir politika kabul ettiyse ve sunucu sürümü ondan yeniyse true.</summary>
        public bool IsPrivacyChanged()
        {
            var saved = PrivacyVersion;
            if (saved == 0) return false;
            return policy_version > saved;
        }

        public void SetPrivacy() => PrivacyVersion = policy_version;
    }
}
