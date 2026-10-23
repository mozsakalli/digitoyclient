// World.Session: oturum akışı — LoginAsync (Guest/Facebook) → ConnectAsync → LoadSessionAsync (definitions ‖ PlayerState ‖ oyun
// loader'ları → masadaysam tam görünüm) → SessionReady. Reconnect de aynı LoadSessionAsync'i koşar.
// Kural: sunucu istemcinin ne bildiğini bilmez; her oturum yüklemesinde PlayerState + (CurrentTableId>0 ise) masa tam görünümü
// ÇEKİLİR, "aynam taze mi" tahmini yok. SessionReady olana kadar push'lar Push() kapısında düşer.
// Olaylar: Ev.Connecting (auth sürüyor) → Ev.Connected → Ev.SessionLoading → Ev.SessionReady (+ reconnect'te Ev.Reconnected).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy
{
    public static partial class World
    {
        /// <summary>Oturum (definitions + PlayerState + masa) yüklü mü. Reconnect/kopuşta false'a düşer, yeniden yüklenince true.</summary>
        public static bool SessionReady { get; private set; }
        static bool _wasReconnecting;
        static readonly List<Func<Task>> SessionLoaders = new();

        /// <summary>Guest + Facebook login. Apple ve AccountConflict çözümü (keep) Faz 5'te core.upm'den alınır.</summary>
        public static async Task<Result> LoginAsync(LoginType type = LoginType.Guest)
        {
            if (type is not (LoginType.Guest or LoginType.Facebook)) return Result.Failure(ErrorCode.InternalError);

            string fbToken = null;
            if (type == LoginType.Facebook)
            {
                // Facebook plugin'i (Assets/Plugins/Facebook); host yoksa (editör mock'suz, desktop) Unavailable → FacebookAuthFailed.
                var fb = await Plugin.Get<Facebook>().Login();
                switch (fb.Status)
                {
                    case Facebook.LoginStatus.Success: fbToken = fb.Token; break;
                    case Facebook.LoginStatus.Cancel: return Result.Failure(ErrorCode.FacebookAuthCancelled);
                    default:
                        Debug.LogWarning($"Facebook login failed: {fb.Status} {fb.Error}");
                        return Result.Failure(ErrorCode.FacebookAuthFailed);
                }
            }

            var req = GetOrCreateAuthRequest();
            req.FbToken = fbToken;
            req.AppleToken = null;
            // Sosyal token'lı istekte eski JWT fast-path'i sosyal girişi sessizce guest hesaba döndürür (core.upm notu) → temizle.
            if (fbToken != null) req.Token = null;
            _pendingLoginType = type;

            var loginResult = await ConnectAsync();
            if (!loginResult.Ok) return loginResult;

            // Son başarılı device key + yöntemi sakla (widevine<->android_id flip-flop'una karşı).
            DeviceKeyHelper.SavePersistentDeviceKey(_cachedAuthRequest.Params[AuthRequestParams.DeviceKey], _cachedAuthRequest.Params[AuthRequestParams.DeviceKeyMethod]);

            Prefs.LastAuthType.Value = type;
            Prefs.Save();

            // Auth ok → oturumun kalanı (definitions + state + oyun loader'ları + masa tam görünümü).
            return await LoadSessionAsync();
        }

        /// <summary>LoadSessionAsync sırasında definitions/state ile paralel koşan oyuna özel yükleyici. Hatası login'i düşürmez.</summary>
        public static void AddSessionLoader(Func<Task> loader)
        {
            if (loader != null && !SessionLoaders.Contains(loader)) SessionLoaders.Add(loader);
        }

        /// <summary>
        /// Auth sonrası hat: definitions + PlayerState + kayıtlı loader'lar paralel; masadaysam tam görünüm; sonra SessionReady.
        /// Definitions/state hatası ölümcül (Result.Failure); loader ve masa tazeleme hataları loglanır, tolere edilir.
        /// </summary>
        public static async Task<Result> LoadSessionAsync(CancellationToken ct = default)
        {
            Begin(); Emit(Ev.SessionLoading); End();

            var defTask = Config.LoadDefinitionsAsync(ct);
            var stateTask = LoadPlayerAsync();
            var loaderTasks = new List<Task>(SessionLoaders.Count);
            for (var i = 0; i < SessionLoaders.Count; i++)
            {
                try { loaderTasks.Add(SessionLoaders[i]()); }
                catch (Exception e) { Debug.LogWarning($"Session loader threw synchronously: {e.Message}"); }
            }

            var def = await defTask;
            var state = await stateTask;
            try { await Task.WhenAll(loaderTasks); }
            catch (Exception e) { Debug.LogWarning($"Session loader failed: {e.Message}"); }

            if (!def.Ok || !state.Ok)
                return Result.Failure(!def.Ok ? def.Error : state.Error);

            // Sunucuya göre masadayım → tam görünüm (cevap reconcile olarak aynaya iner); değilsem ayna düşer.
            if (CurrentTableId > 0)
            {
                var refresh = await RefreshAsync();
                if (!refresh.Ok) Debug.LogWarning($"Table refresh after login failed: {refresh.Error}");
            }
            else DropTable();

            Begin();
            SessionReady = true;
            Dirty(WorldPart.Connection);
            Emit(Ev.SessionReady);
            if (_wasReconnecting) { _wasReconnecting = false; Emit(Ev.Reconnected); }
            End();
            return Result.Success();
        }

        /// <summary>Oturum hazırsa hemen döner; değilse SessionReady olayını bekler. İptalde false.</summary>
        public static async Task<bool> WaitSessionReadyAsync(CancellationToken ct = default)
        {
            if (SessionReady) return true;

            var tcs = new TaskCompletionSource<bool>();
            void Handler(WorldEvent _) => tcs.TrySetResult(true);

            On(Ev.SessionReady, Handler);
            try
            {
                if (SessionReady) return true;
                using (ct.CanBeCanceled ? ct.Register(() => tcs.TrySetResult(false)) : default) // corelib: IAsyncDisposable yok
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                Off(Ev.SessionReady, Handler);
            }
        }

        /// <summary>Logout / oyun değişimi: reconnect bayrağı düşer ki taze login sahte Reconnected üretmesin.</summary>
        static void ResetConnection() { _wasReconnecting = false; SessionReady = false; }
    }
}
