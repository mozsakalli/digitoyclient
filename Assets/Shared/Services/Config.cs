// core.upm Components/Config/Config.cs adaptasyonu. Fark: ConfigureAwait/MainThreadAsync yok (tek-thread); UnityWebRequest →
// motor Http.GetAsync + Wait.Timeout; JsonHelper → motor Json. Settings akışı (02 §B2 istemci kısmı) 2026-10-17.
using System;
using System.Threading;
using System.Threading.Tasks;
using M;
using DigitoyEngine;

namespace Digitoy
{
    public static class Config
    {
        private static GameSettings _cachedGameSettings;
        public static GameSettings Settings => _cachedGameSettings;
        private static EconomyDefinitions _cachedDefinitions;
        internal static EconomyDefinitions CachedDefinitions => _cachedDefinitions;

        /// <summary>Bellekteki tanımları/ayarları düşür (disk cache durur). <see cref="ServiceRegistry"/> ResetSession/ResetAll'da çağırır.</summary>
        internal static void Reset()
        {
            _cachedDefinitions = null;
            _cachedGameSettings = null;
        }

        public static async Task<Result> LoadDefinitionsAsync(CancellationToken ct = default)
        {
            var request = new ConfigRequest
            {
                Key = ConfigKey.Definitions,
                Version = LocalCache.DefinitionsVersion
            };
            var result = await Server.SendRequestAsync<ConfigResult>(Endpoint.ConfigGet, request, ct);

            if (!result.Ok)
            {
                Debug.LogWarning($"Failed to load definitions: {result.Error}");
                _cachedDefinitions = LocalCache.DefinitionsData;
                return Result.Failure(result.Error);
            }

            if (result.Data?.Definitions != null)
            {
                _cachedDefinitions = LocalCache.SaveDefinitions(result.Data);
                Debug.Log("Definitions loaded [SERVER]");
            }
            else
            {
                _cachedDefinitions = LocalCache.DefinitionsData;
                Debug.Log("Definitions loaded [CACHE]");
            }
            return Result.Success();
        }

        /// <summary>Oturum içi uzak ayar (ConfigKey.Control) — JSON gövde T'ye çözülür; parse edilemezse varsayılan T.</summary>
        public static async Task<Result<T>> LoadRemoteSettingsAsync<T>(CancellationToken ct = default) where T : class, new()
        {
            var result = await Server.SendRequestAsync<ConfigResult>(Endpoint.ConfigGet, new ConfigRequest { Key = ConfigKey.Control }, ct);
            if (!result.Ok)
                return Result<T>.Failure(result.Error);

            T parsed = null;
            var json = result.Data?.Json;
            if (!string.IsNullOrEmpty(json))
            {
                try { parsed = Json.FromJson<T>(json); }
                catch (Exception ex) { Debug.LogWarning($"Remote settings parse failed: {ex.Message}"); }
            }
            if (parsed != null)
            {
                Debug.Log("Remote settings loaded [SERVER]");
                return Result<T>.Success(parsed);
            }

            Debug.LogWarning("Failed to parse remote settings JSON, using defaults");
            return Result<T>.Success(new T());
        }

        /// <summary>Login öncesi: force update / maintenance / policy + gateway defteri. Sunucu ve cache yoksa varsayılanla devam, Failure döner ama akış durmaz.</summary>
        public static async Task<Result> LoadGameSettingsAsync(CancellationToken ct = default)
        {
            // Cache verisi yoksa sürüm 0: sunucu settings'i tam göndersin ("settings":null ile boş kalmayalım)
            var localVersion = LocalCache.HasSettingsData ? LocalCache.SettingsVersion : 0;
            var json = await FetchSettingsAsync(localVersion, timeoutSeconds: 15, rounds: 2, ct);
            if (json != null)
            {
                _cachedGameSettings = LocalCache.ApplySettingsResponse(json);
                var source = json.Contains("\"settings\":null") ? "CACHE" : "SERVER";
                Debug.Log($"Game Settings loaded [{source}] gateways={ServerDirectory.Gateways.Count}");
                return Result.Success();
            }

            _cachedGameSettings = LocalCache.SettingsData ?? new GameSettings();
            Debug.LogWarning("Game Settings loaded [CACHE] after retries exhausted");
            return Result.Failure(ErrorCode.InternalError);
        }

        /// <summary>Gateway listesi tükenince bağlanmadan önce hızlı tazeleme (tek tur, kısa timeout). Başarısızlık sessiz: gömülü host'a düşülür.</summary>
        internal static async Task RefreshDirectoryAsync(CancellationToken ct = default)
        {
            var json = await FetchSettingsAsync(LocalCache.HasSettingsData ? LocalCache.SettingsVersion : 0, timeoutSeconds: 5, rounds: 1, ct);
            if (json != null) _cachedGameSettings = LocalCache.ApplySettingsResponse(json);
        }

        /// <summary>Bootstrap adreslerini (saklı seed'ler → gömülü) sırayla dener; ilk 200 cevabın gövdesi. null = hepsi başarısız.</summary>
        private static async Task<string> FetchSettingsAsync(long localVersion, int timeoutSeconds, int rounds, CancellationToken ct)
        {
            var net = Server.Settings;
            if (net == null || Game.Current == null) return null;
            var gameId = Game.Current.gameId;
            var urls = ServerDirectory.BootstrapUrls(net);
            for (var round = 1; round <= rounds; round++)
            {
                foreach (var baseUrl in urls)
                {
                    if (ct.IsCancellationRequested) return null;
                    try
                    {
                        var uid = Guid.NewGuid().ToString("N");
                        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        var url = $"{baseUrl}/api/config/{gameId}/settings?version={localVersion}&uid={uid}&t={ts}";

                        using var timeoutCts = Wait.Timeout(timeoutSeconds);
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        var resp = await Http.GetAsync(url, linked.Token);
                        if (ct.IsCancellationRequested) return null;

                        // 304: yalnız eski sunucu (yeni sunucu hep 200 + "settings":null)
                        if (resp.Status == 304) return "{\"settings\":null,\"version\":" + localVersion + "}";
                        if (resp.Ok && !string.IsNullOrEmpty(resp.Text))
                            return resp.Text;

                        Debug.LogWarning($"Settings {baseUrl} (round {round}) failed: {resp.Status}");
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Settings {baseUrl} (round {round}) exception: {ex.Message}");
                    }
                }
                if (round < rounds) await Wait.Seconds(1f, ct);
            }
            return null;
        }
    }
}
