// core.upm Components/Player/PlayerPrefsService.cs adaptasyonu. Fark: UnityEngine.PlayerPrefs → DigitoyEngine.PlayerPrefs
// (aynı imzalar); servis değil statik Prefs; Notification özelliği Faz 5'e kaldı; LoginType burada tanımlı.
using System;
using DigitoyEngine;

namespace Digitoy
{
    public enum LoginType
    {
        Guest,
        Facebook,
        Apple,
        InstantGame,
        Web
    }

    public class PrefBool
    {
        private bool? _cache;
        private readonly string _key;
        private readonly bool _defaultValue;

        public PrefBool(string key, bool defaultValue = true)
        {
            _key = key;
            _defaultValue = defaultValue;
        }

        public bool Value
        {
            get => _cache ??= PlayerPrefs.GetInt(_key, _defaultValue ? 1 : 0) == 1;
            set
            {
                if (_cache == value) return;
                _cache = value;
                PlayerPrefs.SetInt(_key, value ? 1 : 0);
            }
        }

        public static implicit operator bool(PrefBool p) => p.Value;
    }

    public class PrefInt
    {
        private int? _cache;
        private readonly string _key;
        private readonly int _defaultValue;

        public PrefInt(string key, int defaultValue = 0)
        {
            _key = key;
            _defaultValue = defaultValue;
        }

        public int Value
        {
            get => _cache ??= PlayerPrefs.GetInt(_key, _defaultValue);
            set
            {
                if (_cache == value) return;
                _cache = value;
                PlayerPrefs.SetInt(_key, value);
            }
        }

        public static implicit operator int(PrefInt p) => p.Value;
    }

    public class PrefString
    {
        private string _cache;
        private bool _loaded;
        private readonly string _key;
        private readonly string _defaultValue;

        public PrefString(string key, string defaultValue = "")
        {
            _key = key;
            _defaultValue = defaultValue;
        }

        public string Value
        {
            get
            {
                if (!_loaded)
                {
                    _cache = PlayerPrefs.GetString(_key, _defaultValue);
                    _loaded = true;
                }
                return _cache;
            }
            set
            {
                if (_cache == value && _loaded) return;
                _cache = value;
                _loaded = true;
                PlayerPrefs.SetString(_key, value);
            }
        }

        public static implicit operator string(PrefString p) => p.Value;
    }

    public class PrefEnum<T> where T : struct, Enum
    {
        private T? _cache;
        private readonly string _key;

        public PrefEnum(string key)
        {
            _key = key;
        }

        public T? Value
        {
            get
            {
                if (_cache.HasValue) return _cache;
                if (!PlayerPrefs.HasKey(_key)) return null;
                _cache = (T)(object)PlayerPrefs.GetInt(_key);
                return _cache;
            }
            set
            {
                _cache = value;
                if (value.HasValue)
                    PlayerPrefs.SetInt(_key, (int)(object)value.Value);
                else
                    PlayerPrefs.DeleteKey(_key);
            }
        }
    }

    /// <summary>Cihaz kapsamlı tercihler: oyuncu/oyun değişiminde sıfırlanmaz (tasarım gereği). Statik; kurulum gerekmez.</summary>
    public static class Prefs
    {
        public static readonly PrefBool Music = new("music");
        public static readonly PrefBool Sound = new("sound");
        public static readonly PrefBool Vibrate = new("vibrate");
        public static readonly PrefString Language = new("language", null);
        public static readonly PrefBool GameRequests = new("game_requests");
        public static readonly PrefBool FriendRequest = new("friend_request");
        public static readonly PrefBool PushNotification = new("push_notification");
        public static readonly PrefEnum<LoginType> LastAuthType = new("last_login_type");

        public static void Save() => PlayerPrefs.Save();
    }
}
