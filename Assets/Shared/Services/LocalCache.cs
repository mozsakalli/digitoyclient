// core.upm Helpers/LocalCache.cs adaptasyonu. Fark: UnityEngine.PlayerPrefs → motor; settings JSON motor Json ile
// (GameSettings ICodecModel değil → PlayerPrefs'e Codec değil ham JSON string yazılır); LastPlayed/Blocked blob'ları
// Faz 5. Token oyun başına saklanır (oyun değişiminde relogin; decisions 2026-10-16) — bellekte, cold start yeniden auth.
using System;
using System.Collections.Generic;
using DigitoyEngine;
using M;

namespace Digitoy
{
    /// <summary>GET /api/config/{g}/settings cevabı. settings: sürüm eşitse null.</summary>
    [Serializable]
    public class GameSettingsResponse
    {
        public GameSettings settings;
        public long version;
        public string[] gateways;
        public string[] seeds;
        public long serverTime;
    }

    public static class LocalCache
    {
        private const string VersionKey = "config_version_";
        private const string DataKey = "config_data_";

        private static readonly Dictionary<int, string> _tokens = new();

        public static long DefinitionsVersion => GetVersion(ConfigKey.Definitions);
        public static long SettingsVersion => GetVersion(ConfigKey.Settings);
        public static bool HasToken => !string.IsNullOrEmpty(GetToken());
        public static bool HasSettingsData => !string.IsNullOrEmpty(PlayerPrefs.GetString(Scoped(DataKey + ConfigKey.Settings.ToKey()), null));
        public static EconomyDefinitions DefinitionsData => Load<EconomyDefinitions>(ConfigKey.Definitions);

        public static GameSettings SettingsData
        {
            get
            {
                var json = PlayerPrefs.GetString(Scoped(DataKey + ConfigKey.Settings.ToKey()), null);
                if (string.IsNullOrEmpty(json)) return new GameSettings();
                try { return Json.FromJson<GameSettings>(json) ?? new GameSettings(); }
                catch { return new GameSettings(); }
            }
        }

        private static string Scoped(string key) => key + (Game.Current?.gameId ?? 0);

        public static long GetVersion(ConfigKey key)
        {
            var str = PlayerPrefs.GetString(Scoped(VersionKey + key.ToKey()), "0");
            return long.TryParse(str, out var v) ? v : 0;
        }

        public static T Load<T>(ConfigKey key) where T : class, new()
        {
            var base64 = PlayerPrefs.GetString(Scoped(DataKey + key.ToKey()), null);
            if (string.IsNullOrEmpty(base64)) return new T();
            try
            {
                var codec = new Codec(isClient: true);
                codec.BeginDecoding(Convert.FromBase64String(base64));
                return codec.DecodeObject() as T ?? new T();
            }
            catch { return new T(); }
        }

        /// <summary>Settings cevabını işler: gateway/seed defterini günceller, settings geldiyse cache'e yazar, null (sürüm eşit) ise cache'tekini döner.</summary>
        public static GameSettings ApplySettingsResponse(string json)
        {
            JsonNode root;
            try { root = Json.Parse(json); }
            catch (Exception ex) { Debug.LogWarning($"Settings parse failed: {ex.Message}"); return SettingsData; }
            if (root == null || !root.IsObject) return SettingsData;

            ServerDirectory.Apply(ReadStrings(root, "gateways"), ReadStrings(root, "seeds"));

            JsonNode settingsNode = null;
            foreach (var kv in root.Fields)
                if (kv.Key == "settings") { settingsNode = kv.Value; break; }
            if (settingsNode == null || settingsNode.IsNull || !settingsNode.IsObject) return SettingsData;

            GameSettings settings;
            try { settings = Json.FromNode<GameSettings>(settingsNode); }
            catch (Exception ex) { Debug.LogWarning($"Settings decode failed: {ex.Message}"); return SettingsData; }
            if (settings == null) return SettingsData;

            var k = ConfigKey.Settings.ToKey();
            PlayerPrefs.SetString(Scoped(VersionKey + k), root.GetLong("version").ToString());
            PlayerPrefs.SetString(Scoped(DataKey + k), settingsNode.ToJson());
            PlayerPrefs.Save();
            return settings;
        }

        private static string[] ReadStrings(JsonNode obj, string key)
        {
            foreach (var kv in obj.Fields)
            {
                if (kv.Key != key) continue;
                var arr = kv.Value;
                if (arr == null || !arr.IsArray) return null;
                var list = new List<string>(arr.Count);
                foreach (var item in arr.Items)
                {
                    var s = item.AsString();
                    if (!string.IsNullOrEmpty(s)) list.Add(s);
                }
                return list.ToArray();
            }
            return null;
        }

        public static EconomyDefinitions SaveDefinitions(ConfigResult result)
        {
            var defs = result.Definitions;
            if (defs == null) return new EconomyDefinitions();
            Save(ConfigKey.Definitions, result.Version, defs);
            return defs;
        }

        private static void Save(ConfigKey key, long version, object data)
        {
            var codec = new Codec(isClient: true);
            codec.BeginEncoding();
            codec.EncodeObject(data);
            var k = key.ToKey();
            PlayerPrefs.SetString(Scoped(VersionKey + k), version.ToString());
            PlayerPrefs.SetString(Scoped(DataKey + k), Convert.ToBase64String(codec.GetBuffer().ToArray()));
            PlayerPrefs.Save();
        }

        // JWT yalnız bellekte (oturum ömrü); soğuk açılış deviceKey ile yeniden auth olur.
        public static string GetToken() => _tokens.GetValueOrDefault(Game.Current?.gameId ?? 0);

        public static void SaveToken(string token)
        {
            var g = Game.Current?.gameId ?? 0;
            if (string.IsNullOrEmpty(token)) _tokens.Remove(g);
            else _tokens[g] = token;
        }

        public static void ClearToken(bool stopped = false) => _tokens.Remove(Game.Current?.gameId ?? 0);

        public static void SaveBlob(string name, object data)
        {
            if (data == null) { PlayerPrefs.DeleteKey(name); PlayerPrefs.Save(); return; }
            var codec = new Codec(isClient: true);
            codec.BeginEncoding();
            codec.EncodeObject(data);
            PlayerPrefs.SetString(name, Convert.ToBase64String(codec.GetBuffer().ToArray()));
            PlayerPrefs.Save();
        }

        public static T LoadBlob<T>(string name) where T : class
        {
            var b64 = PlayerPrefs.GetString(name, null);
            if (string.IsNullOrEmpty(b64)) return null;
            try
            {
                var codec = new Codec(isClient: true);
                codec.BeginDecoding(Convert.FromBase64String(b64));
                return codec.DecodeObject() as T;
            }
            catch { return null; }
        }

        public static void DeleteBlob(string name)
        {
            PlayerPrefs.DeleteKey(name);
            PlayerPrefs.Save();
        }
    }
}
