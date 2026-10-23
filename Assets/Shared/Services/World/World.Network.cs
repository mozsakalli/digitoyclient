// World.Network: sunucu kapısının kablo tarafı — bağlantı durumu (tek sahip), soket + auth-on-open, gateway hop + toplam bütçe +
// settings tazeleme, reconnect döngüsü (backoff, internet probu, Dead/Killed), istek gönderimi, push → Push(). Eski Server.cs'nin
// akışı birebir taşındı; tek bildirim kanalı World olayları (Ev.Connecting/Connected/Reconnecting/Disconnected/Killed/Dead).
// Taşıma (tek url'ye bağlan, framing, requestId eşleme, heartbeat) NetworkClient'ta; burada yalnız karar.
// - Reconnect: ilk deneme ANINDA (backoff 0/1/2), kopuşta ön internet probu yok — probe yalnız deneme başarısız olunca.
//   Denemeler arası Connection Reconnecting'de kalır. Auth geri gelince oturum yeniden yüklenir (World.Session), sonra SessionReady.
// - Facebook login = Plugin.Get<Facebook>(); Apple/InstantGame dalları yok. AccountConflict çözümü (keepPlayerId) henüz yok.
// - CarrierInfo/EmulatorDetector/AppsFlyer/Adjust/DeepLink/PlayIntegrity Faz 5 (native köprü).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Digitoy.Network;
using DigitoyEngine;
using M;

namespace Digitoy
{
    public enum ConnectionState
    {
        Idle,         // Hiç bağlanmadı (ya da tam sıfırlandı)
        Connecting,   // İlk ConnectAsync sürüyor
        Healthy,      // Bağlı + kimlik doğrulandı
        Reconnecting, // Bağlantı koptu, kurtarma deneniyor
        Dead,         // Denemeler bitti / internet yok
        Killed        // Terminal (Logout / Unauthorized / Kicked / AnotherLogin / süre doldu)
    }

    /// <summary>ErrorCode.AccountConflict payload'u (sunucu Auth.md kural 3 #8/#9). Faz 5'te sosyal login ile çözülür.</summary>
    public class AccountConflictInfo
    {
        public LoginType Type;
        public AccountSummary Current;
        public AccountSummary Target;
    }

    public static partial class World
    {
        /// <summary>Aktif ağ ayarları (ortam, gateway'ler, dev override).</summary>
        public static NetworkSettings Net { get; private set; }
        public static ConnectionState Connection { get; private set; } = ConnectionState.Idle;
        public static AccountConflictInfo LastAccountConflict { get; private set; }
        public static bool IsAuthenticated => _client?.IsAuthenticated == true;
        public static float LatencyMs => _client?.LatencyMs ?? 0;
        /// <summary>Açık bağlantının WS adresi (gateway seçimi sonrası); bağlı değilse boş.</summary>
        public static string ConnectedUrl => _client?.ConnectedUrl ?? string.Empty;
        public static bool IsConnected => _client?.IsConnected == true;
        public static bool IsReconnecting => Connection == ConnectionState.Reconnecting;
        public static bool IsInitialized => _client != null;

        // ── Bağlantı / reconnect ayarı ───────────────────────────────────────
        /// <summary>Bir bağlanma çağrısında denenecek en fazla gateway (bütçe daha önce biterse daha az).</summary>
        const int MaxGatewayHops = 3;
        /// <summary>Bir bağlanma denemesinin toplam bütçesi (gateway hop'ları + en fazla bir settings tazelemesi dahil).</summary>
        public const int ConnectionTimeoutSeconds = 10;
        /// <summary>Tek istek/yanıt zaman aşımı (NetworkClient).</summary>
        public const int RequestTimeoutSeconds = 30;
        // Her deneme en fazla ConnectionTimeoutSeconds; backoff 0+1+2 → en kötü toplam ≈ 33 s, sonra Dead.
        const int MaxAttempts = 3;
        static readonly int[] BackoffSeconds = { 0, 1, 2 };

        static NetworkClient _client;
        static AuthRequest _cachedAuthRequest;
        static LoginType _pendingLoginType = LoginType.Guest;
        static Task _reconnectTask;

        // ── kurulum ──────────────────────────────────────────────────────────

        /// <summary>Kabloyu kurar; oyun değişiminde de çağrılır (Game.Switch). Tüm modeller düşer, dinleyiciler kalır.</summary>
        public static void Initialize(NetworkSettings settings)
        {
            if (_client != null)
            {
                try { _client.OnDisconnected -= HandleDisconnected; } catch { /* yoksay */ }
                try { _client.OnEvent -= Push; } catch { /* yoksay */ }
                try { _client.OnTick -= Tick; } catch { /* yoksay */ }
                try { _client.Disconnect(); } catch { /* yoksay */ }
                _client = null;
            }

            ResetSession();
            SessionLoaders.Clear();
            _cachedAuthRequest = null;
            _reconnectTask = null;
            LastAccountConflict = null;
            _pendingLoginType = LoginType.Guest;
            SetConnection(ConnectionState.Idle);

            Net = settings;
            _client = new NetworkClient(authenticator: AuthenticateOnOpenAsync);
            _client.OnDisconnected += HandleDisconnected;
            _client.OnEvent += Push;
            _client.OnTick += Tick;
            GetOrCreateAuthRequest();
        }

        /// <summary>Bağlantı durumu tek buradan değişir; fark → olay.</summary>
        static void SetConnection(ConnectionState s)
        {
            if (s == Connection) return;
            var prev = Connection;
            Connection = s;
            Begin();
            Dirty(WorldPart.Connection);
            switch (s)
            {
                case ConnectionState.Connecting: Emit(Ev.Connecting); break;
                case ConnectionState.Reconnecting: _wasReconnecting = true; SessionReady = false; break;
                case ConnectionState.Healthy: Emit(new WorldEvent(Ev.Connected, amount: prev == ConnectionState.Reconnecting ? 1 : 0)); break;
                case ConnectionState.Dead: Emit(Ev.Dead); break;
            }
            End();
        }

        /// <summary>Kopuş bildirimi (Killed terminal, diğerleri Dead). SessionReady düşer.</summary>
        static void EmitDisconnected(CloseReason reason, bool noInternet, bool killed)
        {
            Begin();
            SessionReady = false;
            Dirty(WorldPart.Connection);
            Emit(new WorldEvent(killed ? Ev.Killed : Ev.Disconnected, reason: reason, flag: noInternet));
            End();
        }

        static AuthRequest GetOrCreateAuthRequest()
        {
            var deviceKey = DeviceKeyHelper.GetPersistentDeviceKey();
            DeviceKeyHelper.GetPersistentDeviceKeyFromStorage(out var storedKey, out var storedMethod);
            var migrationKey = DeviceKeyHelper.GetDeviceKey();
            // Dev override: başka test hesabı. Stored/migration anahtarları da bastırılır, yoksa sunucu eski oyuncuyu onlardan bulur.
            if (!string.IsNullOrEmpty(Net.OverrideDeviceKey))
            {
                deviceKey = Net.OverrideDeviceKey;
                storedKey = null; storedMethod = null; migrationKey = null;
                Debug.Log($"[dev] device key override aktif ({deviceKey[..8]}…)");
            }
            else if (GameHost.IsDev) Debug.Log($"[dev] device key override yok (IsDev={GameHost.IsDev}, devDeviceKey='{Net.devDeviceKey}')");
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
                Mig(0, migrationKey);
                _cachedAuthRequest = new AuthRequest
                {
                    GameId = Game.Current.gameId,
                    // Dev override'da eski oturumun JWT'si gönderilmez; yoksa sunucu token fast-path ile eski oyuncuya bağlar.
                    Token = string.IsNullOrEmpty(Net.OverrideDeviceKey) ? LocalCache.GetToken() : null,
                    Params = p
                };
            }
            return _cachedAuthRequest;
        }

        public static AuthRequest GetAuthRequest() => GetOrCreateAuthRequest();

        // ── kopuş / reconnect ────────────────────────────────────────────────

        static void HandleDisconnected(CloseReason reason)
        {
            try
            {
                if (Connection != ConnectionState.Healthy) return;
                // Oturum verisi çevrimdışıyken bayatlar; yeniden yüklenene kadar push'lar Push() kapısında düşer.
                SessionReady = false;

                // Ağla ilgisi olmayan nedenler — reconnect yok, UI'ı "internet yok" diye yanıltma.
                if (reason is CloseReason.Logout
                    or CloseReason.Unauthorized
                    or CloseReason.AnotherLogin
                    or CloseReason.Kicked
                    or CloseReason.IdleTimeout)
                {
                    SetConnection(ConnectionState.Killed);
                    EmitDisconnected(reason, noInternet: false, killed: true);
                    return;
                }

                // Soket öldü → hemen kurtarmaya başla; internet var mı sorusu ilk deneme başarısız olursa sorulur.
                StartReconnect();
            }
            catch (Exception ex)
            {
                Debug.LogError($"World.HandleDisconnected threw: {ex}");
            }
        }

        /// <summary>Reconnect döngüsünü başlatır (zaten çalışmıyorsa). İdempotent.</summary>
        static void StartReconnect()
        {
            if (_reconnectTask is { IsCompleted: false }) return;
            _reconnectTask = RunReconnectLoopAsync();
        }

        /// <summary>Kendi kendine yeten reconnect döngüsü. Çıkış: Healthy+SessionReady, Dead+noInternet ya da Dead (denemeler bitti).</summary>
        static async Task RunReconnectLoopAsync()
        {
            SetConnection(ConnectionState.Reconnecting);
            try
            {
                for (var attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    Debug.Log($"Reconnect attempt {attempt}/{MaxAttempts}");
                    Begin(); Emit(new WorldEvent(Ev.Reconnecting, amount: attempt, aux: MaxAttempts)); End();

                    var backoff = BackoffSeconds[attempt - 1];
                    if (backoff > 0)
                    {
                        try { await NetworkDelay.WaitAsync(TimeSpan.FromSeconds(backoff)); }
                        catch { /* yoksay */ }
                    }

                    Result result;
                    try { result = await ConnectAsync(); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Reconnect attempt {attempt} threw: {ex.Message}");
                        result = Result.Failure(ErrorCode.InternalError);
                    }

                    if (result.Ok)
                    {
                        // Auth geri geldi; oturumu yeniden yükle (PlayerState + masa tam görünümü) → SessionReady/Reconnected.
                        var session = await LoadSessionAsync();
                        if (!session.Ok) Debug.LogWarning($"Session reload after reconnect failed: {session.Error}");
                        if (session.Ok && Connection == ConnectionState.Healthy) return;

                        // Killed/Dead: HandleDisconnected zaten bildirdi — sessizce dur.
                        if (Connection is ConnectionState.Killed or ConnectionState.Dead) return;

                        // Yükleme başarısız ya da soket yükleme sırasında düştü: başarısız deneme say.
                        SetConnection(ConnectionState.Reconnecting);
                        continue;
                    }

                    Debug.Log($"Reconnect attempt {attempt} failed: {result.Error}");
                    if (result.Error != ErrorCode.NoInternet) continue;
                    SetConnection(ConnectionState.Dead);
                    EmitDisconnected(CloseReason.Aborted, noInternet: true, killed: false);
                    return;
                }

                // Tüm denemeler bitti, internet hâlâ var → sunucu/uygulama sorunu.
                SetConnection(ConnectionState.Dead);
                EmitDisconnected(CloseReason.Aborted, noInternet: false, killed: false);
            }
            finally
            {
                _reconnectTask = null;
            }
        }

        public static async Task<bool> WaitForServerReady()
        {
            await NetworkDelay.WaitAsync(TimeSpan.FromMilliseconds(100));
            if (Connection == ConnectionState.Healthy && IsAuthenticated) return true;
            switch (Connection)
            {
                case ConnectionState.Killed:
                case ConnectionState.Idle:
                    return false;
                case ConnectionState.Dead:
                    StartReconnect();
                    break;
            }

            if (_reconnectTask == null) return Connection == ConnectionState.Healthy && IsAuthenticated;
            try { await _reconnectTask; }
            catch { /* yoksay */ }

            return Connection == ConnectionState.Healthy && IsAuthenticated;
        }

        // ── bağlanma ─────────────────────────────────────────────────────────

        // NetworkClient.OnOpen içinde koşar; bağlantı yalnız null (başarı) dönerse başarılı sayılır.
        static async Task<ErrorCode?> AuthenticateOnOpenAsync(CancellationToken ct)
        {
            try
            {
                var req = GetOrCreateAuthRequest();
                var result = await _client.SendRequestAsync<AuthResult>(Endpoint.AuthLogin, req, ct);
                if (result == null) return ErrorCode.InternalError;
                LastAccountConflict = result.Error == ErrorCode.AccountConflict
                    ? new AccountConflictInfo { Type = _pendingLoginType, Current = result.Current, Target = result.Target }
                    : null;
                if (!result.Ok && result.Error != ErrorCode.AlreadyLoggedIn) return result.Error;

                SetAuthId(result.PlayerId);
                if (result.Token != null) LocalCache.SaveToken(result.Token);
                _cachedAuthRequest.Token = LocalCache.GetToken();
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Auth-on-open failed: {ex.Message}");
                return ErrorCode.InternalError;
            }
        }

        /// <summary>Soket + auth. Başarısızlıkta internet probu ile NoInternet ayrımı yapılır. Healthy → Ev.Connected.</summary>
        public static async Task<Result> ConnectAsync(bool relogin = false)
        {
            if (_client == null) return Result.Failure(ErrorCode.InternalError);
            // Idle/Killed'dan gelen = taze login; Healthy (relogin) / Reconnecting / Dead = kurtarma.
            var reconnecting = Connection is ConnectionState.Healthy or ConnectionState.Reconnecting or ConnectionState.Dead;
            SetConnection(reconnecting ? ConnectionState.Reconnecting : ConnectionState.Connecting);
            var error = await ConnectTransportAsync(relogin);
            if (error.HasValue)
            {
                var noInternet = !await InternetProbe.HasInternetAsync();
                // Kurtarmada Dead kararı döngünün (denemeler arası durum titremesin); taze login'de Idle.
                SetConnection(reconnecting ? ConnectionState.Reconnecting : ConnectionState.Idle);
                return Result.Failure(noInternet ? ErrorCode.NoInternet : error.Value);
            }

            SetConnection(ConnectionState.Healthy);
            return Result.Success();
        }

        /// <summary>
        /// Gateway'ler sırayla: soket hiç açılamadıysa sonrakine geç. Tek toplam bütçe (ConnectionTimeoutSeconds; tüm hop'lar
        /// + en fazla bir settings tazelemesi dahil). Soket açılıp auth reddedildiyse hop yapılmaz, hata döner.
        /// </summary>
        static async Task<ErrorCode?> ConnectTransportAsync(bool relogin, CancellationToken ct = default)
        {
            var net = Net;
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

        // ── istek ────────────────────────────────────────────────────────────

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

        // ── uygulama yaşam döngüsü ───────────────────────────────────────────

        public static void OnAppPaused() { }

        public static void OnAppResumed()
        {
            // Arka plandayken ölen sağlıklı oturumu kurtar. Connecting'de ikinci ConnectAsync başlatma.
            if (IsConnected) return;
            if (Connection is ConnectionState.Killed or ConnectionState.Dead or ConnectionState.Idle or ConnectionState.Connecting) return;
            StartReconnect();
        }

        /// <summary>Kullanıcının "İnternet yok / sunucuya ulaşılamıyor" UI'ından yeniden deneme.</summary>
        public static void Reconnect()
        {
            if (Connection != ConnectionState.Dead && Connection != ConnectionState.Killed) return;
            StartReconnect();
        }

        /// <summary>Bağlantıyı terminal olarak kapat (Killed); olay üretmez (HandleDisconnected Healthy dışını yok sayar).</summary>
        public static void Disconnect(CloseReason reason = default)
        {
            SetConnection(ConnectionState.Killed);
            _client?.Disconnect(reason);
        }

        /// <summary>Çıkış: bağlantı Logout nedeniyle kapanır (Ev.Killed), tüm modeller düşer.</summary>
        public static void Logout()
        {
            _client?.Disconnect(CloseReason.Logout);
            ResetSession();
        }
    }
}
