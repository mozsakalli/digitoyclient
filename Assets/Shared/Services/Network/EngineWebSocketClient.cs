// core.upm Network/BestWebSocketClient.cs adaptasyonu: taşıma katmanı Best HTTP yerine motor WebSocket.
// Korunan mantık: gateway hop + toplam bütçe, requestId→TCS eşleme, 25 sn uygulama heartbeat'i, auth-on-open + grace,
// Connection nesli ile bayat callback filtresi, tek noktadan HandleClose, CloseReason çözümü.
// Farklar: motor tek-thread → kilit/Interlocked/volatile yok; Task.Delay/CancelAfter yerine Wait.Timeout/Frame.Next
// (thread-pool timer devamı ana thread dışına düşerdi); protokol ping/CloseAfterNoMessage motor soketinde yok (sunucu 60 sn
// uygulama çerçevesi kuralı heartbeat ile karşılanır); RunContinuationsAsynchronously kaldırıldı (thread-pool'a kaçardı).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy.Network
{
    internal sealed class EngineWebSocketClient : INetworkClient
    {
        private static readonly TimeSpan AuthFailGracePeriod = TimeSpan.FromSeconds(3);
        // Sunucu 60 sn UYGULAMA çerçevesi görmezse bağlantıyı düşürür — gönderim boşta kalınca Endpoint.Ping at.
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);
        /// <summary>Bir ConnectAsync çağrısında denenecek en fazla gateway sayısı (bütçe daha önce biterse daha az).</summary>
        private const int MaxGatewayHops = 3;

        private readonly NetworkSettings _net;

        /// <summary>Soket açılınca koşan kimlik adımı: null = başarı, ErrorCode = ret.</summary>
        private readonly Func<INetworkClient, CancellationToken, Task<ErrorCode?>>? _authenticator;

        private readonly Codec _codec = new(isClient: true);
        private byte[] _rx = new byte[4096]; // WsMessage native tampon -> Codec byte[] (buyuyen, mesaj basina alloc yok)

        /// <summary>Bağlantı başına durum; her ConnectAsync'te değişir, bayat callback'ler referans kıyasıyla elenir.</summary>
        private sealed class Connection
        {
            public readonly int Generation;
            public readonly WebSocket Socket;
            public readonly string Url;
            public readonly TaskCompletionSource<ErrorCode?> ConnectTcs = new();
            public readonly CancellationTokenSource ConnectCts;
            public bool IsOpen;
            public bool IsAuthenticated;
            public CloseReason PendingCloseReason = CloseReason.Normal;
            public bool CloseHandled;

            public Connection(int generation, WebSocket socket, string url, CancellationTokenSource cts)
            {
                Generation = generation;
                Socket = socket;
                Url = url;
                ConnectCts = cts;
            }
        }

        private Connection? _conn;
        private int _connectionCounter;
        private DateTime _lastSendUtc;

        private long _nextRequestId;
        private readonly Dictionary<long, TaskCompletionSource<object?>> _pending = new();

        public event Action<CloseReason>? OnDisconnected;
        public event Action<object>? OnEvent;

        public bool IsConnected
        {
            get
            {
                var c = _conn;
                return c != null && c.IsOpen && c.Socket.State == WebSocketState.Open;
            }
        }

        public string ConnectedUrl
        {
            get
            {
                var c = _conn;
                return c != null && c.IsOpen ? c.Url : string.Empty;
            }
        }

        // Motor soketi gecikme ölçmez; Faz 1'de Ping gidiş-dönüşünden hesaplanır.
        public int LatencyMs => 0;

        public EngineWebSocketClient(NetworkSettings net,
            Func<INetworkClient, CancellationToken, Task<ErrorCode?>>? authenticator = null)
        {
            _net = net;
            _authenticator = authenticator;
        }

        // ── Bağlan ────────────────────────────────────────────────────────────

        public async Task<ErrorCode?> ConnectAsync(CancellationToken cancellationToken = default, bool relogin = false)
        {
            // Gateway'ler sırayla: soket hiç açılamadıysa sonrakine geç. Tek toplam bütçe (connectionTimeoutSeconds, tüm hop'lar
            // + en fazla bir settings tazelemesi dahil): bütçe bitince hata döner.
            var budget = TimeSpan.FromSeconds(_net.connectionTimeoutSeconds);
            var deadline = DateTime.UtcNow + budget;
            var refreshed = false;
            ErrorCode? error = ErrorCode.InternalError;
            for (var hop = 0; hop < MaxGatewayHops; hop++)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero || cancellationToken.IsCancellationRequested) break;

                if (!_net.HasCustomServerUrl && ServerDirectory.NeedsRefresh && !refreshed)
                {
                    refreshed = true;
                    // Tazeleme bütçenin en fazla yarısını yesin
                    using var refreshTimeout = Wait.Timeout((float)(remaining.TotalSeconds / 2));
                    using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, refreshTimeout.Token);
                    try { await Config.RefreshDirectoryAsync(refreshCts.Token); }
                    catch (Exception ex) { Debug.LogWarning($"Directory refresh failed: {ex.Message}"); }
                    remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero) break;
                }

                var url = ServerDirectory.WebSocketUrl(_net);
                var (err, opened) = await ConnectOnceAsync(url, remaining, cancellationToken, relogin);
                error = err;
                if (error == null || opened || cancellationToken.IsCancellationRequested) return error;
                Debug.LogWarning($"Gateway unreachable: {url}");
                if (!ServerDirectory.MarkUnreachable(url)) break;
            }
            Debug.LogWarning($"Connect failed within {budget.TotalSeconds:0}s budget");
            return error;
        }

        private async Task<(ErrorCode? error, bool opened)> ConnectOnceAsync(string url, TimeSpan timeout, CancellationToken cancellationToken, bool relogin)
        {
            var existing = _conn;
            if (relogin) TeardownSilently(existing);
            if (existing != null && existing.IsOpen && existing.IsAuthenticated) return (null, true);
            TeardownSilently(existing);

            var timeoutCts = Wait.Timeout((float)timeout.TotalSeconds);
            var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            Debug.Log($"Connecting {url}");
            WebSocket socket;
            try
            {
                socket = await WebSocket.ConnectAsync(url, cts.Token);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"ConnectAsync: {ex.Message}");
                cts.Dispose(); timeoutCts.Dispose();
                return (ErrorCode.InternalError, false);
            }
            if (socket == null || socket.State != WebSocketState.Open)
            {
                Debug.LogWarning($"Socket not open: {socket?.Error ?? "null"}");
                try { socket?.Dispose(); } catch { /* yoksay */ }
                cts.Dispose(); timeoutCts.Dispose();
                return (ErrorCode.InternalError, false);
            }

            var gen = ++_connectionCounter;
            var conn = new Connection(gen, socket, url, cts);
            _conn = conn;

            socket.OnMessage = m => OnMessage(conn, m);
            socket.OnClose = (code, reason) => HandleClose(conn, code, reason);
            // Zaman aşımı/iptal bağlantı görevini hatayla bitirir (zaten bittiyse no-op).
            cts.Token.Register(static state =>
            {
                ((TaskCompletionSource<ErrorCode?>)state!).TrySetResult(ErrorCode.InternalError);
            }, conn.ConnectTcs);

            OnOpen(conn);

            var result = await conn.ConnectTcs.Task;
            timeoutCts.Dispose();
            // Soket açıldı (opened=true): auth reddi ≠ ulaşılamayan gateway
            return (result, true);
        }

        // ── Kapat ─────────────────────────────────────────────────────────────

        public void Disconnect(CloseReason reason = CloseReason.Normal)
        {
            var conn = _conn;
            if (conn == null) return;
            if (conn.CloseHandled) return;

            conn.PendingCloseReason = reason;

            if (conn.IsOpen && conn.Socket.State == WebSocketState.Open)
            {
                try
                {
                    _ = conn.Socket.CloseAsync(1000, ((int)reason).ToString());
                    // HandleClose, kapanış çerçevesi gidip gelince OnClose ile koşar.
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Disconnect close failed: {ex.Message}");
                    HandleClose(conn, 1006, string.Empty);
                }
            }
            else
            {
                HandleClose(conn, 1000, ((int)reason).ToString());
            }
        }

        // ── Gönder ────────────────────────────────────────────────────────────

        public async Task<TResponse?> SendRequestAsync<TResponse>(
            int endpoint, object? request,
            CancellationToken cancellationToken = default) where TResponse : class
        {
            var conn = _conn;
            if (conn == null || !conn.IsOpen || conn.Socket.State != WebSocketState.Open) return null;

            var requestId = ++_nextRequestId;
            var tcs = new TaskCompletionSource<object?>();
            _pending[requestId] = tcs;

            using var timeoutCts = Wait.Timeout(_net.requestTimeoutSeconds);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            using var reg = cts.Token.Register(() =>
            {
                _pending.Remove(requestId);
                tcs.TrySetCanceled();
            });

            try
            {
                _codec.BeginEncoding();
                _codec.Encode(requestId);
                _codec.Encode((long)endpoint);
                _codec.EncodeObject(request);
                var seg = _codec.GetBuffer();
                var bytes = new byte[seg.Count];
                Buffer.BlockCopy(seg.Array!, seg.Offset, bytes, 0, seg.Count);
                if (!conn.Socket.Send(bytes))
                {
                    _pending.Remove(requestId);
                    Debug.LogWarning($"Send ep={endpoint}: socket refused");
                    return null;
                }
                _lastSendUtc = DateTime.UtcNow;

                return await tcs.Task as TResponse;
            }
            catch (Exception ex)
            {
                _pending.Remove(requestId);
                tcs.TrySetCanceled();
                if (ex is not OperationCanceledException)
                    Debug.LogWarning($"Send ep={endpoint}: {ex.Message}");
                return null;
            }
        }

        // ── Soket olayları ────────────────────────────────────────────────────

        private void OnOpen(Connection conn)
        {
            if (!IsCurrent(conn)) return;

            conn.IsOpen = true;
            Debug.Log("Transport opened");

            if (_authenticator == null)
            {
                conn.IsAuthenticated = true;
                conn.ConnectTcs.TrySetResult(null);
                _ = HeartbeatAsync(conn);
                return;
            }

            _ = AuthenticateAsync(conn);
        }

        private async Task AuthenticateAsync(Connection conn)
        {
            CancellationToken token;
            try { token = conn.ConnectCts.Token; }
            catch (ObjectDisposedException) { token = CancellationToken.None; }

            ErrorCode? authError;
            try
            {
                authError = await _authenticator!(this, token);
            }
            catch (OperationCanceledException)
            {
                authError = ErrorCode.InternalError;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Auth failed: {ex.Message}");
                authError = ErrorCode.InternalError;
            }

            if (!IsCurrent(conn) || !conn.IsOpen)
            {
                // Auth sürerken soket kapandı — HandleClose koştu ya da koşacak.
                conn.ConnectTcs.TrySetResult(authError ?? ErrorCode.InternalError);
                return;
            }

            if (authError == null)
            {
                conn.IsAuthenticated = true;
                Debug.Log("Authenticated");
                conn.ConnectTcs.TrySetResult(null);
                _ = HeartbeatAsync(conn);
            }
            else
            {
                Debug.LogWarning($"Auth rejected: {authError}");
                conn.ConnectTcs.TrySetResult(authError);
                // Sunucu normalde özel CloseReason ile kapatır; kapatmazsa biz kapatırız (soket sızmasın).
                _ = AuthFailFallbackAsync(conn);
            }
        }

        // Bağlantı başına döngü; bağlantı bayatlayınca ya da kapanınca çıkar. Frame tabanlı (tek-thread).
        private async Task HeartbeatAsync(Connection conn)
        {
            _lastSendUtc = DateTime.UtcNow;
            while (IsCurrent(conn) && conn.IsOpen && conn.Socket.State == WebSocketState.Open)
            {
                if (!await Frame.Next()) return;
                if (!IsCurrent(conn) || !conn.IsOpen || conn.Socket.State != WebSocketState.Open) return;
                if (DateTime.UtcNow - _lastSendUtc < HeartbeatInterval) continue;
                try { await SendRequestAsync<object>(Endpoint.Ping, null); }
                catch { /* soket ölümü OnClose ile gelir */ }
            }
        }

        private async Task AuthFailFallbackAsync(Connection conn)
        {
            try { await NetworkDelay.WaitAsync(AuthFailGracePeriod); }
            catch { /* yoksay */ }

            if (!IsCurrent(conn) || !conn.IsOpen || conn.IsAuthenticated) return;
            if (conn.CloseHandled) return;

            Debug.LogWarning("Auth-fail fallback: server did not close, closing locally.");
            try { _ = conn.Socket.CloseAsync(1000, string.Empty); }
            catch { HandleClose(conn, 1006, string.Empty); }
        }

        private void OnMessage(Connection conn, WsMessage msg)
        {
            if (!IsCurrent(conn)) return;

            try
            {
                if (msg.IsText || msg.Length == 0) return;

                // Mesaj native bellekte (zero-GC); Codec byte[] istiyor -> yeniden kullanilan tampona tek kopya.
                if (_rx.Length < msg.Length) _rx = new byte[Math.Max(msg.Length, _rx.Length * 2)];
                msg.Bytes.CopyTo(_rx);
                _codec.BeginDecoding(_rx, msg.Length, 0);
                var requestId = _codec.TryDecodeNumber();
                var response = _codec.DecodeObject();

                // Gelen paket tipi ve ham boyut (debug)
                Debug.Log($"RX type={response?.GetType().Name ?? "null"} len={msg.Length} reqId={requestId}");

                if (requestId == -1) { OnEvent?.Invoke(response!); return; }
                if (requestId == 0) return; // protokol ping cevabı

                if (_pending.Remove(requestId, out var tcs)) tcs.TrySetResult(response);
            }
            catch (Exception ex)
            {
                Debug.LogError($"OnMessage: {ex} (gen={conn.Generation}, len={msg.Length})");
            }
        }

        // ── Kapanış (tek doğruluk noktası) ────────────────────────────────────

        /// <summary>Bağlantıyı tam olarak bir kez kapatır: connect TCS'i çözer, bekleyen istekleri iptal eder, soketi bırakır, OnDisconnected atar.</summary>
        private void HandleClose(Connection conn, int code, string message)
        {
            if (conn.CloseHandled) return;
            conn.CloseHandled = true;

            var reason = ResolveCloseReason(conn.PendingCloseReason, code, message);
            Debug.Log($"Closed gen={conn.Generation} code={code} reason={reason} msg={message}");

            if (ReferenceEquals(_conn, conn)) _conn = null;

            conn.IsOpen = false;
            conn.IsAuthenticated = false;

            DetachSocket(conn.Socket);

            conn.ConnectTcs.TrySetResult(ErrorCode.InternalError);
            try { conn.ConnectCts.Dispose(); } catch { /* yoksay */ }

            CancelPending();

            var handler = OnDisconnected;
            if (handler == null) return;
            try { handler.Invoke(reason); }
            catch (Exception ex) { Debug.LogError($"OnDisconnected subscriber threw: {ex}"); }
        }

        /// <summary>Yeni bağlantı başlatırken ya da kurulum hatasında sessiz yıkım — OnDisconnected atmaz.</summary>
        private void TeardownSilently(Connection? conn)
        {
            if (conn == null) return;
            if (conn.CloseHandled) return;
            conn.CloseHandled = true;

            if (ReferenceEquals(_conn, conn)) _conn = null;
            conn.IsOpen = false;
            conn.IsAuthenticated = false;
            DetachSocket(conn.Socket);
            conn.ConnectTcs.TrySetResult(ErrorCode.InternalError);
            try { conn.ConnectCts.Dispose(); } catch { /* yoksay */ }
            CancelPending();
        }

        private static CloseReason ResolveCloseReason(CloseReason pending, int code, string message)
        {
            // 1) Yerel niyet kazanır (Disconnect(reason)).
            if (pending != CloseReason.Normal) return pending;

            // 2) Sunucu CloseReason'ı doğrudan WS durum koduna yazar.
            if (Enum.IsDefined(typeof(CloseReason), code))
                return (CloseReason)code;

            // 3) Eski sunucu yolu: int açıklama string'inde.
            if (int.TryParse(message, out var parsed) && Enum.IsDefined(typeof(CloseReason), parsed))
                return (CloseReason)parsed;

            // 4) 1006 = anormal kapanış.
            return code == 1006 ? CloseReason.Aborted : CloseReason.Normal;
        }

        private static void DetachSocket(WebSocket socket)
        {
            try
            {
                socket.OnMessage = null;
                socket.OnClose = null;
            }
            catch { /* yoksay */ }

            try { _ = socket.CloseAsync(); } catch { /* yoksay */ }
            try { socket.Dispose(); } catch { /* yoksay */ }
        }

        // ── Yardımcılar ───────────────────────────────────────────────────────

        private bool IsCurrent(Connection conn) => ReferenceEquals(_conn, conn);

        private void CancelPending()
        {
            if (_pending.Count == 0) return;
            var snapshot = new TaskCompletionSource<object?>[_pending.Count];
            _pending.Values.CopyTo(snapshot, 0);
            _pending.Clear();
            foreach (var tcs in snapshot) tcs.TrySetCanceled();
        }
    }
}
