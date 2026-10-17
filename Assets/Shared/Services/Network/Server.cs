// core.upm Network/Server.cs adaptasyonu. Farklar (davranış korunur):
// - BestWebSocketClient → EngineWebSocketClient; Awaitable.MainThreadAsync yok (motor tek-thread); UnityEditor kancaları yok.
// - GameProperties → NetworkSettings (Server.Settings) + Game.Current.gameId.
// - SystemInfo/Application → Platform shim; CarrierInfo/EmulatorDetector/AppsFlyer/Adjust/DeepLink/PlayIntegrity/RemoteContent
//   Faz 5 (native köprü) — satırları kaldırıldı.
// - Facebook/Apple/InstantGame login dalları silindi (decisions 2026-10-16); yalnız Guest. AccountConflict çözümü de o zaman.
// - LoadSessionAsync: Inbox/Messaging/Friend loader'ları Faz 5; definitions + player state + oyun loader'ları kaldı.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Digitoy.Network;
using M;
using DigitoyEngine;

namespace Digitoy;

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
    public static bool IsAuthenticated => _client?.IsConnected == true && _isAuthenticated;
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

    private static INetworkClient _client;
    private static bool _isAuthenticated;
    private static AuthRequest _cachedAuthRequest;

    // ── Reconnect ayarı ──────────────────────────────────────────────────
    // Her deneme en fazla connectionTimeoutSeconds (10 s, gateway hop'ları dahil); backoff 1+2+4 → en kötü toplam ≈ 37 s, sonra Dead.
    private const int MaxAttempts = 3;
    private static readonly int[] BackoffSeconds = { 1, 2, 4 };

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

        _isAuthenticated = false;
        _cachedAuthRequest = null;
        _reconnectTask = null;
        LastAccountConflict = null;
        _pendingLoginType = LoginType.Guest;
        State = ConnectionState.Idle;

        Settings = settings;
        _client = new EngineWebSocketClient(settings, authenticator: AuthenticateOnOpenAsync);
        _client.OnDisconnected += HandleDisconnected;
        _client.OnEvent += HandleEvent;
        _cachedAuthRequest = null;
        GetOrCreateAuthRequest();
    }

    private static async void HandleDisconnected(CloseReason reason)
    {
        try
        {
            if (State != ConnectionState.Healthy) return;
            _isAuthenticated = false;
            // Oturum verisi çevrimdışıyken bayatlayabilir; LoadSessionAsync yeniden bitene kadar TableState push'ları kapılı.
            IsSessionReady = false;

            // Ağla ilgisi olmayan nedenler — probe yapma, UI'ı "internet yok" diye yanıltma.
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

            State = ConnectionState.Reconnecting;
            SafeInvoke(OnReconnecting, 0, MaxAttempts); // probe sırasında "hazırlanıyor" sinyali
            var noInternet = !await InternetProbe.HasInternetAsync();
            if (noInternet)
            {
                State = ConnectionState.Dead;
                SafeInvoke(OnDisconnected, reason, true);
                return;
            }

            // İnternet var ama soket öldü → reconnect hattına devret.
            StartReconnect();
        }
        catch (Exception ex)
        {
            // async void: yakalanmayan istisna süreci düşürür.
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

                try { await NetworkDelay.WaitAsync(TimeSpan.FromSeconds(BackoffSeconds[attempt - 1])); }
                catch { /* yoksay */ }

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
                    continue;
                }

                Debug.Log($"Reconnect attempt {attempt} failed: {result.Error}");
                if (result.Error != ErrorCode.NoInternet) continue;
                OnDisconnected?.Invoke(CloseReason.Aborted, true);
                return;
            }

            // Tüm denemeler bitti, internet hâlâ var → sunucu/uygulama sorunu.
            State = ConnectionState.Dead;
            OnDisconnected?.Invoke(CloseReason.Aborted, false);
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

    // EngineWebSocketClient.OnOpen içinde koşar; bağlantı yalnız null (başarı) dönerse başarılı sayılır.
    private static async Task<ErrorCode?> AuthenticateOnOpenAsync(INetworkClient client, CancellationToken ct)
    {
        try
        {
            var req = GetOrCreateAuthRequest();
            var result = await client.SendRequestAsync<AuthResult>(Endpoint.AuthLogin, req, ct);
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
            _isAuthenticated = true;
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Auth-on-open failed: {ex.Message}");
            return ErrorCode.InternalError;
        }
    }

    private static LoginType _pendingLoginType = LoginType.Guest;

    /// <summary>Guest login. Sosyal sağlayıcılar ve AccountConflict çözümü (keep) Faz 5'te core.upm'den alınır.</summary>
    public static async Task<Result> LoginAsync(LoginType type = LoginType.Guest)
    {
        if (type != LoginType.Guest) return Result.Failure(ErrorCode.InternalError);
        OnLoginProgress?.Invoke(LoginStep.Auth);

        var guestReq = GetOrCreateAuthRequest();
        guestReq.FbToken = null;
        guestReq.AppleToken = null;
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

    public static async Task<Result> ConnectAsync(LoginType type = LoginType.Guest, bool relogin = false,
        bool suppressConnectedEvent = false)
    {
        if (_client == null) return Result.Failure(ErrorCode.InternalError);
        State = State == ConnectionState.Idle ? ConnectionState.Connecting : ConnectionState.Reconnecting;
        var reconnecting = State == ConnectionState.Reconnecting;
        var error = await _client.ConnectAsync(relogin: relogin);
        if (error.HasValue)
        {
            var noInternet = !await InternetProbe.HasInternetAsync();
            State = reconnecting ? ConnectionState.Dead : ConnectionState.Idle;
            Debug.Log(State);
            return Result.Failure(noInternet ? ErrorCode.NoInternet : error.Value);
        }

        State = ConnectionState.Healthy;
        // Reconnect döngüsü event'i bastırır ve LoadSessionAsync SONRASI OnConnected(true)'yu kendisi atar.
        if (!suppressConnectedEvent) OnConnected?.Invoke(reconnecting);
        return Result.Success();
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
        _isAuthenticated = false;
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
