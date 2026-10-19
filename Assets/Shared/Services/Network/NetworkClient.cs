// Tek WS bağlantısının taşıma + protokol katmanı (core.upm BestWebSocketClient soyundan; INetworkClient kaldırıldı,
// tek implementasyon). Sorumluluk: TEK url'ye bağlan, auth-on-open (+ grace), requestId→TCS eşleme, istek zaman aşımı,
// 25 sn uygulama heartbeat'i, Connection nesli ile bayat callback filtresi, tek noktadan HandleClose.
// Gateway seçimi / hop / bütçe / settings tazeleme BURADA DEĞİL — Server.ConnectTransportAsync (uygulama kararı).
// Zero-GC: gönderim Codec tamponundan Span ile kopyasız; istek başına yalnız TCS; zaman aşımı per-request CTS yerine
// saniyelik tek tick'te süpürülür; alım yeniden kullanılan tampona tek kopya; frame döngüsü yok (Wait.Seconds).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy.Network
{
    internal sealed class NetworkClient
    {
        private static readonly TimeSpan AuthFailGracePeriod = TimeSpan.FromSeconds(3);
        // Sunucu 60 sn UYGULAMA çerçevesi görmezse bağlantıyı düşürür — gönderim boşta kalınca Endpoint.Ping at.
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);
        private const float TickSeconds = 1f;

        /// <summary>Soket açılınca koşan kimlik adımı: null = başarı, ErrorCode = ret.</summary>
        private readonly Func<CancellationToken, Task<ErrorCode?>>? _authenticator;

        private readonly Codec _codec = new(isClient: true);
        private byte[] _rx = new byte[4096]; // WsMessage native tampon -> Codec byte[] (büyüyen, mesaj başına alloc yok)

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
        private readonly Dictionary<long, (TaskCompletionSource<object?> tcs, DateTime deadline)> _pending = new();
        // Süpürme için yeniden kullanılan listeler: önce toplanır, sözlükten düşülür, SONRA tetiklenir (TCS devamı eşzamanlı koşar).
        private readonly List<long> _expired = new();
        private readonly List<TaskCompletionSource<object?>> _toCancel = new();

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

        public bool IsAuthenticated => IsConnected && _conn!.IsAuthenticated;

        public string ConnectedUrl
        {
            get
            {
                var c = _conn;
                return c != null && c.IsOpen ? c.Url : string.Empty;
            }
        }

        /// <summary>Son heartbeat Ping gidiş-dönüşü (ms); ilk ping'e kadar 0.</summary>
        public int LatencyMs { get; private set; }

        public NetworkClient(Func<CancellationToken, Task<ErrorCode?>>? authenticator = null)
        {
            _authenticator = authenticator;
        }

        // ── Bağlan ────────────────────────────────────────────────────────────

        /// <summary>
        /// Verilen adrese tek deneme. <c>opened=false</c>: soket hiç açılamadı (gateway ulaşılamaz → çağıran sıradakine geçer);
        /// <c>opened=true</c> + hata: soket açıldı ama auth reddedildi / zaman aşımı. Açık + auth'lu bağlantı varsa ve relogin
        /// değilse hemen başarı döner.
        /// </summary>
        public async Task<(ErrorCode? error, bool opened)> ConnectAsync(string url, TimeSpan timeout, CancellationToken cancellationToken, bool relogin)
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
            // Zaman aşımı/iptal bağlantı görevini hatayla bitirir (zaten bittiyse etkisiz).
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

        /// <summary>İstek-cevap. null: bağlı değil / gönderilemedi / zaman aşımı / iptal / bağlantı düştü.</summary>
        public async Task<TResponse?> SendRequestAsync<TResponse>(
            int endpoint, object? request,
            CancellationToken cancellationToken = default) where TResponse : class
        {
            var conn = _conn;
            if (conn == null || !conn.IsOpen || conn.Socket.State != WebSocketState.Open) return null;

            var requestId = ++_nextRequestId;
            var tcs = new TaskCompletionSource<object?>();
            _pending[requestId] = (tcs, DateTime.UtcNow + TimeSpan.FromSeconds(Server.RequestTimeoutSeconds));

            // Dış iptal: yalnız iptal edilebilir token için kayıt (alloc yalnız o durumda).
            var reg = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() => { _pending.Remove(requestId); tcs.TrySetCanceled(); })
                : default;

            try
            {
                _codec.BeginEncoding();
                _codec.Encode(requestId);
                _codec.Encode((long)endpoint);
                _codec.EncodeObject(request);
                var seg = _codec.GetBuffer();
                // Kopyasız gönderim: motor soketi Span alır, Codec tamponu bir sonraki Encode'a kadar bize ait.
                if (!conn.Socket.Send(new ReadOnlySpan<byte>(seg.Array!, seg.Offset, seg.Count)))
                {
                    _pending.Remove(requestId);
                    Debug.LogWarning($"Send ep={endpoint}: socket refused");
                    return null;
                }
                _lastSendUtc = DateTime.UtcNow;

                return await tcs.Task as TResponse;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Send ep={endpoint}: {ex.Message}");
                return null;
            }
            finally
            {
                _pending.Remove(requestId);
                reg.Dispose();
            }
        }

        // ── Soket olayları ────────────────────────────────────────────────────

        private void OnOpen(Connection conn)
        {
            if (!IsCurrent(conn)) return;

            conn.IsOpen = true;
            Debug.Log("Transport opened");
            _ = TickAsync(conn);

            if (_authenticator == null)
            {
                conn.IsAuthenticated = true;
                conn.ConnectTcs.TrySetResult(null);
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
                authError = await _authenticator!(token);
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
            }
            else
            {
                Debug.LogWarning($"Auth rejected: {authError}");
                conn.ConnectTcs.TrySetResult(authError);
                // Sunucu normalde özel CloseReason ile kapatır; kapatmazsa biz kapatırız (soket sızmasın).
                _ = AuthFailFallbackAsync(conn);
            }
        }

        /// <summary>Bağlantı başına saniyelik tick: istek zaman aşımı süpürmesi + boşta heartbeat. Bağlantı bayatlayınca çıkar.</summary>
        private async Task TickAsync(Connection conn)
        {
            _lastSendUtc = DateTime.UtcNow;
            while (IsCurrent(conn) && conn.IsOpen && conn.Socket.State == WebSocketState.Open)
            {
                await Wait.Seconds(TickSeconds);
                if (!IsCurrent(conn) || !conn.IsOpen || conn.Socket.State != WebSocketState.Open) return;

                var now = DateTime.UtcNow;
                SweepTimeouts(now);
                if (conn.IsAuthenticated && now - _lastSendUtc >= HeartbeatInterval)
                    _ = PingAsync();
            }
        }

        private void SweepTimeouts(DateTime now)
        {
            if (_pending.Count == 0) return;
            foreach (var kv in _pending)
                if (kv.Value.deadline <= now) _expired.Add(kv.Key);
            if (_expired.Count == 0) return;
            Debug.LogWarning($"{_expired.Count} request(s) timed out");
            CancelCollected();
        }

        /// <summary>_expired'daki istekleri sözlükten düşürüp iptal eder; listeler tetiklemeden önce boşaltılır (yeniden giriş güvenli).</summary>
        private void CancelCollected()
        {
            for (var i = 0; i < _expired.Count; i++)
                if (_pending.Remove(_expired[i], out var p)) _toCancel.Add(p.tcs);
            _expired.Clear();
            if (_toCancel.Count == 0) return;
            var n = _toCancel.Count;
            var snapshot = n == 1 ? null : _toCancel.ToArray();
            var single = n == 1 ? _toCancel[0] : null;
            _toCancel.Clear();
            if (single != null) { single.TrySetCanceled(); return; }
            foreach (var tcs in snapshot!) tcs.TrySetCanceled();
        }

        private async Task PingAsync()
        {
            var t0 = DateTime.UtcNow;
            var r = await SendRequestAsync<object>(Endpoint.Ping, null);
            if (r != null) LatencyMs = (int)(DateTime.UtcNow - t0).TotalMilliseconds;
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

                // Mesaj native bellekte (zero-GC); Codec byte[] istiyor -> yeniden kullanılan tampona tek kopya.
                if (_rx.Length < msg.Length) _rx = new byte[Math.Max(msg.Length, _rx.Length * 2)];
                msg.Bytes.CopyTo(_rx);
                _codec.BeginDecoding(_rx, msg.Length, 0);
                var requestId = _codec.TryDecodeNumber();
                var response = _codec.DecodeObject();

                if (requestId == -1) { OnEvent?.Invoke(response); return; }
                if (requestId == 0) return; // protokol ping cevabı

                if (_pending.Remove(requestId, out var p)) p.tcs.TrySetResult(response);
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
            foreach (var kv in _pending) _expired.Add(kv.Key);
            CancelCollected();
        }
    }
}
