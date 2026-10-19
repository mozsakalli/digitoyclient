// core.upm Network/Server.cs adaptasyonu. Farklar (davranış korunur):
// - BestWebSocketClient/INetworkClient → NetworkClient (tek implementasyon, arayüz yok); Awaitable.MainThreadAsync yok (tek-thread).
// - Gateway hop + toplam bütçe + settings tazeleme döngüsü BURADA (ConnectTransportAsync): taşıma katmanı Config'e bağımlı değil.
// - Auth bayrağı tek yerde (NetworkClient.IsAuthenticated); Server kopya tutmaz.
// - Reconnect: ilk deneme ANINDA (backoff 0/1/2), kopuşta ön internet probu yok — probe yalnız deneme başarısız olunca
//   (kopuşların çoğu anlık; 3 HTTPS probu beklemek kurtarmayı geciktiriyordu). Denemeler arası State Reconnecting'de kalır.
// - GameProperties → NetworkSettings (Server.Settings) + Game.Current.gameId; SystemInfo/Application → Platform shim.
// - CarrierInfo/EmulatorDetector/AppsFlyer/Adjust/DeepLink/PlayIntegrity/RemoteContent Faz 5 (native köprü).
// - Facebook login = Plugin.Get<Facebook>() (Assets/Plugins/Facebook, 2026-10-18); Apple/InstantGame dalları yok (decisions 2026-10-16).
//   AccountConflict çözümü (keepPlayerId) henüz yok.
// - LoadSessionAsync: Inbox/Messaging/Friend loader'ları Faz 5; definitions + player state + oyun loader'ları kaldı.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Digitoy.Network;
using M;
using DigitoyEngine;

namespace Digitoy
{
    public enum ConnectionState
    {
        Idle, // Hiç bağlanmadı (ya da tam sıfırlandı)
        Connecting, // İlk ConnectAsync sürüyor
        Healthy, // Bağlı + kimlik doğrulandı
        Reconnecting, // Bağlantı koptu, kurtarma deneniyor
        Dead,
        Killed // Terminal (Logout / Unauthorized / Kicked / AnotherLogin / süre doldu)
    }

    /// <summary>Server.OnLoginProgress ile bildirilen login adımları.</summary>
    public enum LoginStep
    {
        Auth, // kimlik + soket bağlantısı sürüyor
        Loading, // auth ok; definitions + player state + oturum loader'ları sürüyor
        Ready // oturum tamamen yüklendi (IsSessionReady = true)
    }

    /// <summary>ErrorCode.AccountConflict payload'u (sunucu Auth.md kural 3 #8/#9). Faz 5'te sosyal login ile çözülür.</summary>
    public class AccountConflictInfo
    {
        public LoginType Type;
        public AccountSummary Current;
        public AccountSummary Target;
    }

    public static class Server
    {
        public static NetworkSettings Settings;
        public static AccountConflictInfo LastAccountConflict { get; private set; }
        public static ConnectionState State { get; private set; } = ConnectionState.Idle;
        public static bool IsAuthenticated => _client?.IsAuthenticated == true;
        public static float LatencyMs => _client?.LatencyMs ?? 0;
        /// <summary>Açık bağlantının WS adresi (gateway seçimi sonrası); bağlı değilse boş.</summary>
        public static string ConnectedUrl => _client?.ConnectedUrl ?? string.Empty;
        public static bool IsConnected => _client?.IsConnected == true;
        public static bool IsReconnecting => State == ConnectionState.Reconnecting;
        public static bool IsInitialized => _client != null;

        public static event Action<CloseReason, bool> OnDisconnected;
        public static event Action<int, int> OnReconnecting;
        public static event Action<bool> OnConnected;
        public static event Action<OnlineStatsResponse> OnOnlineStats;

        // ── Oturum (auth + definitions + player state) ───────────────────────
        // OnConnected yalnız auth'u garanti eder; IsSessionReady ayrıca definitions + player state (+ oyun loader'ları).
        // Yüklü veri isteyen sahne controller'ları OnConnected yerine WaitForSessionReadyAsync bekler.
        public static bool IsSessionReady { get; private set; }
        public static event Action OnSessionReady;
        public static event Action<LoginStep> OnLoginProgress;
        private static readonly List<Func<Task>> SessionLoaders = new();

        private static NetworkClient _client;
        private static AuthRequest _cachedAuthRequest;
        private static LoginType _pendingLoginType = LoginType.Guest;

        // ── Bağlantı / reconnect ayarı ───────────────────────────────────────
        /// <summary>Bir bağlanma çağrısında denenecek en fazla gateway (bütçe daha önce biterse daha az).</summary>
        private const int MaxGatewayHops = 3;
        /// <summary>Bir bağlanma denemesinin toplam bütçesi (gateway hop'ları + en fazla bir settings tazelemesi dahil).</summary>
        public const int ConnectionTimeoutSeconds = 10;
        /// <summary>Tek istek/yanıt zaman aşımı (NetworkClient).</summary>
        public const int RequestTimeoutSeconds = 30;
        // Her deneme en fazla ConnectionTimeoutSeconds; backoff 0+1+2 → en kötü toplam ≈ 33 s, sonra Dead.
        private const int MaxAttempts = 3;
        private static readonly int[] BackoffSeconds = { 0, 1, 2 };

        private static Task _reconnectTask;

        private static AuthRequest GetOrCreateAuthRequest()
        {
            var deviceKey = DeviceKeyHelper.GetPersistentDeviceKey();
            DeviceKeyHelper.GetPersistentDeviceKeyFromStorage(out var storedKey, out var storedMethod);
#if DEBUG
            if (!string.IsNullOrEmpty(Settings.OverrideDeviceKey))
                deviceKey = Settings.OverrideDeviceKey;
#endif
            if (_cachedAuthRequest == null)
            {
                var p = new Dictionary<string, string>
                {
                    [AuthRequestParams.DeviceKey] = deviceKey,
                    [AuthRequestParams.DeviceKeyMethod] = DeviceKeyHelper.GetPersistentDeviceKeyMethod(),
                    [AuthRequestParams.StoredDeviceKey] = storedKey,
                    [AuthRequestParams.StoredDeviceKeyMethod] = storedMethod,
                    [ClientAuthParams.Model] = Platform.DeviceModel,
                    [AuthRequestParams.OS] = Platform.OS,
                    [AuthRequestParams.AppVersion] = Platform.AppVersion,
                    [AuthRequestParams.Platform] = Platform.Current.ToString(),
                };
                // Eski istemci anahtarları; sıra = sunucu arama önceliği (yalnız deviceKey bulunamazsa bakılır).
                void Mig(int i, string key) { if (!string.IsNullOrEmpty(key)) p[AuthRequestParams.MigrationKey(i)] = key; }
                Mig(0, DeviceKeyHelper.GetDeviceKey());
                _cachedAuthRequest = new AuthRequest
                {
                    GameId = Game.Current.gameId,
                    Token = PlayerService.I.GetToken(),
                    Params = p
                };
            }

            return _cachedAuthRequest;
        }

        /// <summary>Tüm statik durumu sıfırlar; oyun değişiminde de çağrılır (Game.Switch).</summary>
        public static void Initialize(NetworkSettings settings)
        {
            if (_client != null)
            {
                try { _client.OnDisconnected -= HandleDisconnected; } catch { /* yoksay */ }
                try { _client.OnEvent -= HandleEvent; } catch { /* yoksay */ }
                try { _client.Disconnect(); } catch { /* yoksay */ }
                _client = null;
            }

            OnDisconnected = null;
            OnReconnecting = null;
            OnConnected = null;
            OnOnlineStats = null;
            OnSessionReady = null;
            OnLoginProgress = null;
            IsSessionReady = false;
            SessionLoaders.Clear();

            _cachedAuthRequest = null;
            _reconnectTask = null;
            LastAccountConflict = null;
            _pendingLoginType = LoginType.Guest;
            State = ConnectionState.Idle;

            Settings = settings;
            _client = new NetworkClient(authenticator: AuthenticateOnOpenAsync);
            _client.OnDisconnected += HandleDisconnected;
            _client.OnEvent += HandleEvent;
            GetOrCreateAuthRequest();
        }

        private static void HandleDisconnected(CloseReason reason)
        {
            try
            {
                if (State != ConnectionState.Healthy) return;
                // Oturum verisi çevrimdışıyken bayatlayabilir; LoadSessionAsync yeniden bitene kadar TableState push'ları kapılı.
                IsSessionReady = false;

                // Ağla ilgisi olmayan nedenler — reconnect yok, UI'ı "internet yok" diye yanıltma.
                if (reason is CloseReason.Logout
                    or CloseReason.Unauthorized
                    or CloseReason.AnotherLogin
                    or CloseReason.Kicked
                    or CloseReason.IdleTimeout)
                {
                    State = ConnectionState.Killed;
                    SafeInvoke(OnDisconnected, reason, false);
                    return;
                }

                // Soket öldü → hemen kurtarmaya başla; internet var mı sorusu ilk deneme başarısız olursa sorulur.
                StartReconnect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HandleDisconnected threw: {ex}");
            }
        }

        // ── Güvenli event dağıtımı ───────────────────────────────────────────

        /// <summary>Fırlatan dinleyici durum makinesini bozamaz. AOT corelib'de Delegate.GetInvocationList yok →
        /// abone başına izolasyon yerine tek try/catch (fırlatan aboneden sonrakiler o turda atlanır).</summary>
        private static void SafeInvoke<T1, T2>(Action<T1, T2> evt, T1 a1, T2 a2)
        {
            if (evt == null) return;
            try { evt.Invoke(a1, a2); }
            catch (Exception ex) { Debug.LogError($"Event subscriber threw: {ex}"); }
        }

        // ── Reconnect hattı ──────────────────────────────────────────────────

        /// <summary>Reconnect döngüsünü başlatır (zaten çalışmıyorsa). İdempotent.</summary>
        private static void StartReconnect()
        {
            if (_reconnectTask is { IsCompleted: false }) return;
            _reconnectTask = RunReconnectLoopAsync();
        }

        /// <summary>Kendi kendine yeten reconnect döngüsü. Çıkış: Healthy, Dead+noInternet ya da Dead (denemeler bitti).</summary>
        private static async Task RunReconnectLoopAsync()
        {
            State = ConnectionState.Reconnecting;

            try
            {
                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    Debug.Log($"Reconnect attempt {attempt}/{MaxAttempts}");
                    OnReconnecting?.Invoke(attempt, MaxAttempts);

                    var backoff = BackoffSeconds[attempt - 1];
                    if (backoff > 0)
                    {
                        try { await NetworkDelay.WaitAsync(TimeSpan.FromSeconds(backoff)); }
                        catch { /* yoksay */ }
                    }

                    Result result;
                    try
                    {
                        result = await ConnectAsync(suppressConnectedEvent: true);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Reconnect attempt {attempt} threw: {ex.Message}");
                        result = Result.Failure(ErrorCode.InternalError);
                    }

                    if (result.Ok)
                    {
                        // Auth geri geldi; reconnect'i duyurmadan ÖNCE oturumu yeniden yükle ki aboneler taze veri görsün.
                        var session = await LoadSessionAsync();
                        if (!session.Ok)
                            Debug.LogWarning($"Session reload after reconnect failed: {session.Error}");

                        if (session.Ok && State == ConnectionState.Healthy)
                        {
                            OnConnected?.Invoke(true);
                            return;
                        }

                        // Killed/Dead: HandleDisconnected UI'ı zaten bilgilendirdi — sessizce dur.
                        if (State is ConnectionState.Killed or ConnectionState.Dead) return;

                        // Yükleme başarısız ya da soket yükleme sırasında düştü: başarısız deneme say.
                        State = ConnectionState.Reconnecting;
                        continue;
                    }

                    Debug.Log($"Reconnect attempt {attempt} failed: {result.Error}");
                    if (result.Error != ErrorCode.NoInternet) continue;
                    State = ConnectionState.Dead;
                    SafeInvoke(OnDisconnected, CloseReason.Aborted, true);
                    return;
                }

                // Tüm denemeler bitti, internet hâlâ var → sunucu/uygulama sorunu.
                State = ConnectionState.Dead;
                SafeInvoke(OnDisconnected, CloseReason.Aborted, false);
            }
            finally
            {
                _reconnectTask = null;
            }
        }

        public static async Task<bool> WaitForServerReady()
        {
            await NetworkDelay.WaitAsync(TimeSpan.FromMilliseconds(100));
            if (State == ConnectionState.Healthy && IsAuthenticated) return true;
            switch (State)
            {
                case ConnectionState.Killed:
                case ConnectionState.Idle:
                    return false;
                case ConnectionState.Dead:
                    StartReconnect();
                    break;
            }

            if (_reconnectTask == null) return State == ConnectionState.Healthy && IsAuthenticated;
            try { await _reconnectTask; }
            catch { /* yoksay */ }

            return State == ConnectionState.Healthy && IsAuthenticated;
        }

        // NetworkClient.OnOpen içinde koşar; bağlantı yalnız null (başarı) dönerse başarılı sayılır.
        private static async Task<ErrorCode?> AuthenticateOnOpenAsync(CancellationToken ct)
        {
            try
            {
                var req = GetOrCreateAuthRequest();
                var result = await _client.SendRequestAsync<AuthResult>(Endpoint.AuthLogin, req, ct);
                if (result == null)
                    return ErrorCode.InternalError;
                LastAccountConflict = result.Error == ErrorCode.AccountConflict
                    ? new AccountConflictInfo { Type = _pendingLoginType, Current = result.Current, Target = result.Target }
                    : null;
                if (!result.Ok && result.Error != ErrorCode.AlreadyLoggedIn)
                    return result.Error;

                PlayerService.I.ValidateAuthResult(result);

                if (result.Token != null) PlayerService.I.SaveToken(result.Token);
                _cachedAuthRequest.Token = PlayerService.I.GetToken();
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Auth-on-open failed: {ex.Message}");
                return ErrorCode.InternalError;
            }
        }

        /// <summary>Guest + Facebook login. Apple ve AccountConflict çözümü (keep) Faz 5'te core.upm'den alınır.</summary>
        public static async Task<Result> LoginAsync(LoginType type = LoginType.Guest)
        {
            if (type is not (LoginType.Guest or LoginType.Facebook)) return Result.Failure(ErrorCode.InternalError);
            OnLoginProgress?.Invoke(LoginStep.Auth);

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

            var loginResult = await ConnectAsync(type);
            if (!loginResult.Ok) return loginResult;

            // Son başarılı device key + yöntemi sakla (widevine<->android_id flip-flop'una karşı).
            DeviceKeyHelper.SavePersistentDeviceKey(_cachedAuthRequest.Params[AuthRequestParams.DeviceKey], _cachedAuthRequest.Params[AuthRequestParams.DeviceKeyMethod]);

            PlayerPrefsService.I.LastAuthType.Value = type;
            PlayerPrefsService.I.Save();

            // Auth ok → oturumun kalanı (definitions + state + oyun loader'ları + masa tazeleme).
            return await LoadSessionAsync();
        }

        /// <summary>LoadSessionAsync sırasında definitions/state ile paralel koşan oyuna özel yükleyici. Hatası login'i düşürmez.</summary>
        public static void AddSessionLoader(Func<Task> loader)
        {
            if (loader != null && !SessionLoaders.Contains(loader)) SessionLoaders.Add(loader);
        }

        /// <summary>
        /// Auth sonrası hat: definitions + player state + kayıtlı loader'lar paralel, gerekirse masa tazeleme, sonra oturum hazır.
        /// Definitions/state hatası ölümcül (Result.Failure); loader ve masa tazeleme hataları loglanır, tolere edilir.
        /// </summary>
        public static async Task<Result> LoadSessionAsync(CancellationToken ct = default)
        {
            OnLoginProgress?.Invoke(LoginStep.Loading);

            var defTask = Config.LoadDefinitionsAsync(ct);
            var stateTask = PlayerService.I.GetStateAsync();
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

            var table = TableService.I;
            if (table is { Exists: true, NeedRefresh: true })
            {
                var refresh = await table.RefreshAsync();
                if (!refresh.Ok) Debug.LogWarning($"Table refresh after login failed: {refresh.Error}");
            }

            IsSessionReady = true;
            OnSessionReady?.Invoke();
            OnLoginProgress?.Invoke(LoginStep.Ready);
            return Result.Success();
        }

        /// <summary>Oturum hazırsa hemen döner; değilse login hattı bitene kadar bekler. İptalde false.</summary>
        public static async Task<bool> WaitForSessionReadyAsync(CancellationToken ct = default)
        {
            if (IsSessionReady) return true;

            var tcs = new TaskCompletionSource<bool>();
            void Handler() => tcs.TrySetResult(true);

            OnSessionReady += Handler;
            try
            {
                if (IsSessionReady) return true;

                using (ct.CanBeCanceled ? ct.Register(() => tcs.TrySetResult(false)) : default) // corelib: IAsyncDisposable yok
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                OnSessionReady -= Handler;
            }
        }

        /// <summary>Soket + auth. Başarısızlıkta internet probu ile NoInternet ayrımı yapılır. Reconnect döngüsü event'i bastırır.</summary>
        public static async Task<Result> ConnectAsync(LoginType type = LoginType.Guest, bool relogin = false,
            bool suppressConnectedEvent = false)
        {
            if (_client == null) return Result.Failure(ErrorCode.InternalError);
            // Idle/Killed'dan gelen = taze login; Healthy (relogin) / Reconnecting / Dead = kurtarma.
            var reconnecting = State is ConnectionState.Healthy or ConnectionState.Reconnecting or ConnectionState.Dead;
            State = reconnecting ? ConnectionState.Reconnecting : ConnectionState.Connecting;
            var error = await ConnectTransportAsync(relogin);
            if (error.HasValue)
            {
                var noInternet = !await InternetProbe.HasInternetAsync();
                // Kurtarmada Dead kararı döngünün (denemeler arası State titremesin); taze login'de Idle.
                State = reconnecting ? ConnectionState.Reconnecting : ConnectionState.Idle;
                return Result.Failure(noInternet ? ErrorCode.NoInternet : error.Value);
            }

            State = ConnectionState.Healthy;
            // Reconnect döngüsü event'i bastırır ve LoadSessionAsync SONRASI OnConnected(true)'yu kendisi atar.
            if (!suppressConnectedEvent) OnConnected?.Invoke(reconnecting);
            return Result.Success();
        }

        /// <summary>
        /// Gateway'ler sırayla: soket hiç açılamadıysa sonrakine geç. Tek toplam bütçe (ConnectionTimeoutSeconds; tüm hop'lar
        /// + en fazla bir settings tazelemesi dahil). Soket açılıp auth reddedildiyse hop yapılmaz, hata döner.
        /// </summary>
        private static async Task<ErrorCode?> ConnectTransportAsync(bool relogin, CancellationToken ct = default)
        {
            var net = Settings;
            var budget = TimeSpan.FromSeconds(ConnectionTimeoutSeconds);
            var deadline = DateTime.UtcNow + budget;
            var refreshed = false;
            ErrorCode? error = ErrorCode.InternalError;
            for (var hop = 0; hop < MaxGatewayHops; hop++)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || ct.IsCancellationRequested) break;

                if (!net.HasCustomServerUrl && ServerDirectory.NeedsRefresh && !refreshed)
                {
                    refreshed = true;
                    // Tazeleme bütçenin en fazla yarısını yesin
                    using var refreshTimeout = Wait.Timeout((float)(remaining.TotalSeconds / 2));
                    using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct, refreshTimeout.Token);
                    try { await Config.RefreshDirectoryAsync(refreshCts.Token); }
                    catch (Exception ex) { Debug.LogWarning($"Directory refresh failed: {ex.Message}"); }
                    remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                }

                var url = ServerDirectory.WebSocketUrl(net);
                var (err, opened) = await _client.ConnectAsync(url, remaining, ct, relogin);
                error = err;
                if (error == null || opened || ct.IsCancellationRequested) return error;
                Debug.LogWarning($"Gateway unreachable: {url}");
                if (!ServerDirectory.MarkUnreachable(url)) break;
            }
            Debug.LogWarning($"Connect failed within {budget.TotalSeconds:0}s budget");
            return error;
        }

        public static async Task<Result<TResponse>> SendRequestAsync<TResponse>(
            int endpoint, object request, CancellationToken cancellationToken = default)
            where TResponse : class
        {
            if (_client == null || !IsConnected)
                return Result<TResponse>.Failure(ErrorCode.InternalError);

            var resp = await _client.SendRequestAsync<TResponse>(endpoint, request, cancellationToken);
            return resp != null
                ? Result<TResponse>.Success(resp)
                : Result<TResponse>.Failure(ErrorCode.InternalError);
        }

        private static void HandleEvent(object e)
        {
            if (e is TableState tableState)
            {
                // Oturum (yeniden) yüklenirken masa push'larını düşür: bekleyen masa tazeleme cevabı otoriter.
                if (!IsSessionReady) return;
                TableService.I?.HandleServerEvent(tableState);
            }
            else if (e is OnlineStatsResponse onlineStatsResponse)
            {
                OnOnlineStats?.Invoke(onlineStatsResponse);
                LobbyService.I?.HandleOnlineStats(onlineStatsResponse);
            }
            else if (e is EventExecutionResult eventExecutionResult) PlayerService.I?.ApplyEventResult(eventExecutionResult);
            // Faz 5: PlayerPreview (Friend/Messaging), FriendPreviewsResult, IntegrityChallenge.
        }

        public static void OnAppPaused()
        {
        }

        public static void OnAppResumed()
        {
            // Arka plandayken ölen sağlıklı oturumu kurtar. Connecting'de ikinci ConnectAsync başlatma.
            if (IsConnected) return;
            if (State is ConnectionState.Killed or ConnectionState.Dead or ConnectionState.Idle or ConnectionState.Connecting) return;
            StartReconnect();
        }

        public static void Logout()
        {
            _client?.Disconnect(CloseReason.Logout);
            ServiceRegistry.ResetSession();
        }

        public static void ReconnectAsync()
        {
            // Kullanıcının "İnternet yok / sunucuya ulaşılamıyor" UI'ından yeniden deneme.
            if (State != ConnectionState.Dead && State != ConnectionState.Killed) return;
            StartReconnect();
        }

        public static void Disconnect(CloseReason reason = default)
        {
            State = ConnectionState.Killed;
            _client?.Disconnect(reason);
        }

        public static AuthRequest GetAuthRequest() => GetOrCreateAuthRequest();
    }
}
