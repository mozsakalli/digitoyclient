// World.Table: masa aynası. Sunucu deltası (TableState) merge kuralı: null alan = değişmedi, dolu = yaz, liste = komple değiştir,
// TableId≤0 = masadan düştüm, koltuk LastMove PlayerLeft/Kicked = koltuk silinir, PlayerJoined / farklı PlayerId = koltuk komple
// değişir. Tam görünüm = ilk state, reconcile cevabı (Join/Refresh/PlayNow/Create) ya da kendi Hand'im gelmiş (sunucu eli yalnız
// Join/Refresh/Start/Finish/ResetSeat'te yollar) → TableSnapshot. Fark → olaylar: faz, sıra, koltuk, oyuna özel Move.
// Oyun kuralı YOK: World saf aynadır; oyuna özel yorum (el yaması vb.) burada değil, oyunun kendi katmanında yapılır.
// İstekler (PlayNow/Join/Leave/Refresh/Act) de burada: cevap aynı ApplyTable yolundan girer.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy
{
    public static partial class World
    {
        public static TableState Table { get; private set; }
        /// <summary>Her masa uygulamasında artar (Clear dahil).</summary>
        public static int TableVersion { get; private set; }
        /// <summary>Uygulanan paket reconcile (Join/Refresh/PlayNow/Create cevabı ya da oturum yüklemesi) → view animasyonsuz kurar.</summary>
        public static bool Reconciling { get; private set; }

        static long _timeSkew; // sunucu - istemci (ms)
        static readonly List<(int pos, SeatPreview seat)> _left = new(); // tam görünümde düşürülen koltuklar (olay için)

        public static bool HasTable => Table?.TableId > 0;
        public static TablePhase Phase => Table?.Phase ?? TablePhase.None;
        public static TableSubPhase SubPhase => Table?.SubPhase ?? TableSubPhase.None;
        static int MaxSeats => Game.Current?.maxSeats ?? 4;

        public static int MyPosition
        {
            get
            {
                var id = MyId;
                if (Table?.Players == null || id == 0) return -1;
                foreach (var kv in Table.Players) if (kv.Value.PlayerId == id) return kv.Key;
                return -1;
            }
        }
        public static bool IsSpectator => HasTable && MyPosition < 0;
        public static bool IsMyTurn { get { var p = MyPosition; return p >= 0 && Table?.TurnOwner == p; } }
        public static SeatPreview MySeat { get { var p = MyPosition; return p >= 0 ? Table.Players[p] : null; } }
        /// <summary>Elim (sunucunun son yolladığı). Yoksa boş.</summary>
        public static List<int> MyHand => MySeat?.Hand ?? EmptyHand;
        static readonly List<int> EmptyHand = new();

        public static SeatPreview SeatAt(int serverPos) => Table?.Players != null && Table.Players.TryGetValue(serverPos, out var s) ? s : null;
        /// <summary>Sunucu pozisyonu → ekran pozisyonu (0 = ben; izleyicide identity).</summary>
        public static int RelativePosition(int serverPos)
        {
            if (serverPos < 0) return -1;
            var mine = Math.Max(0, MyPosition);
            return (serverPos + MaxSeats - mine) % MaxSeats;
        }
        public static int DataPosition(int relativePos) => (relativePos + Math.Max(0, MyPosition)) % MaxSeats;

        public static float RemainingSeconds(long? at)
        {
            if (at is not > 0) return -1f;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _timeSkew;
            return Math.Max(0, at.Value - now) / 1000f;
        }

        // ── uygulama ─────────────────────────────────────────────────────────────

        static void ApplyTable(TableState inc, bool reconcile)
        {
            if (inc == null) return;
            if (inc.ServerTime is > 0) _timeSkew = inc.ServerTime.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (inc.TableId is <= 0) { ClearTable(kicked: inc.Action == GameAction.PlayerKicked); return; }

            var prev = Table;
            var had = prev?.TableId > 0;
            // Delta TableId taşımaz (null); yalnız DOLU ve farklı TableId masa değişimidir.
            var full = !had || reconcile || (inc.TableId.HasValue && prev.TableId != inc.TableId);
            var prevPhase = had ? prev.Phase : TablePhase.None;
            var prevTurn = had ? prev.TurnOwner ?? -1 : -1;
            var prevMine = had ? MyPosition : -1;
            var myId = MyId;
            var prevTop = had ? Gin.TopDiscard : -1;   // delta ÖNCESİ ıskarta tepesi (PickFromDiscard yansıması için)

            Dirty(WorldPart.Table);
            TableVersion++;
            Reconciling = reconcile || !had;

            _left.Clear();
            if (full)
            {
                if (had && !inc.TableId.HasValue) inc.TableId = prev.TableId;
                Table = inc;
                if (inc.Players != null)
                {
                    // Ayrılmış koltuklar modelden düşer; olayları (SeatLeft / benim koltuğum gitti) aşağıda _left'ten üretilir
                    foreach (var (pos, seat) in inc.Players)
                        if (seat.LastMove is GameAction.PlayerLeft or GameAction.PlayerKicked) _left.Add((pos, seat));
                    foreach (var (pos, _) in _left) inc.Players.Remove(pos);
                }
            }
            else
            {
                var s = prev;
                if (inc.GameId.HasValue) s.GameId = inc.GameId;
                if (inc.TurnNo.HasValue) s.TurnNo = inc.TurnNo;
                if (inc.TurnOwner.HasValue) s.TurnOwner = inc.TurnOwner;
                if (inc.Phase != TablePhase.None) s.Phase = inc.Phase;
                if (inc.SubPhase != TableSubPhase.None) s.SubPhase = inc.SubPhase;
                if (inc.ServerTime.HasValue) s.ServerTime = inc.ServerTime;
                if (inc.TimeoutAt.HasValue) s.TimeoutAt = inc.TimeoutAt;
                if (inc.GameEndsAt.HasValue) s.GameEndsAt = inc.GameEndsAt;
                if (inc.Deck != null) s.Deck = inc.Deck;
                if (inc.DeckCount.HasValue) s.DeckCount = inc.DeckCount;
                if (inc.DeckLastNo.HasValue) s.DeckLastNo = inc.DeckLastNo;
                if (inc.Discards != null) { s.Discards ??= new(); foreach (var kv in inc.Discards) s.Discards[kv.Key] = kv.Value; }
                if (inc.Flags.HasValue) s.Flags = inc.Flags;
                if (inc.Bet.HasValue) s.Bet = inc.Bet;
                if (inc.Jackpot.HasValue) s.Jackpot = inc.Jackpot;
                if (inc.Action.HasValue) s.Action = inc.Action;
                if (inc.PluginState != null) s.PluginState = inc.PluginState;
                if (inc.Spectators != null) MergeSeats(s.Spectators ??= new(), inc.Spectators);
                if (inc.Players != null)
                {
                    MergeSeats(s.Players ??= new(), inc.Players);
                    // kendi Hand'im geldi → sunucu tam görünüm yolladı
                    foreach (var seat in inc.Players.Values) if (seat.PlayerId == myId && seat.Hand != null) { full = true; break; }
                }
            }

            // Koltuktaki ekonomi sonucu (masa ödülü) oyuncu modeline
            var me = MySeat;
            if (me?.EventResult != null) { var r = me.EventResult; me.EventResult = null; ApplyEventResult(r); }

            var mine = MyPosition;

            // ── olaylar ──
            // Tam görünümde benim koltuğum "ayrıldı/atıldı" geldiyse → masadan düştüm (TableEntered üretmeden)
            foreach (var (_, seat) in _left)
                if (seat.PlayerId == myId) { ClearTable(kicked: seat.LastMove == GameAction.PlayerKicked); return; }

            if (!had) Emit(Ev.TableEntered);

            foreach (var (pos, seat) in _left)
                Emit(new WorldEvent(Ev.SeatLeft, pos, seat.PlayerId ?? 0, flag: seat.LastMove == GameAction.PlayerKicked));

            var actor = -1;
            if (inc.Players != null)
                foreach (var (pos, seat) in inc.Players)
                {
                    switch (seat.LastMove)
                    {
                        case GameAction.PlayerJoined: Emit(new WorldEvent(Ev.SeatJoined, pos, seat.PlayerId ?? 0)); break;
                        case GameAction.PlayerLeft: Emit(new WorldEvent(Ev.SeatLeft, pos, seat.PlayerId ?? 0)); break;
                        case GameAction.PlayerKicked: Emit(new WorldEvent(Ev.SeatLeft, pos, seat.PlayerId ?? 0, flag: true)); break;
                        case GameAction.PlayerUpdated:
                            if (!string.IsNullOrEmpty(seat.Message)) Emit(new WorldEvent(Ev.SeatChat, pos, seat.PlayerId ?? 0, text: seat.Message));
                            else Emit(new WorldEvent(Ev.SeatUpdated, pos, seat.PlayerId ?? 0));
                            break;
                        case GameAction.Ready: Emit(new WorldEvent(Ev.SeatReady, pos, seat.PlayerId ?? 0)); break;
                        case null or GameAction.None: break;
                        default: if (inc.Players.Count == 1) actor = pos; break;
                    }
                }
            if (inc.Spectators != null)
                foreach (var (pos, seat) in inc.Spectators)
                    if (seat.LastMove == GameAction.PlayerUpdated && !string.IsNullOrEmpty(seat.Message))
                        Emit(new WorldEvent(Ev.SeatChat, -1, seat.PlayerId ?? 0, text: seat.Message));

            // Oturduğum koltuk gitti (ToWaiting deltası beni "ayrıldı" koltuğuyla taşıyabilir) → masadan düştüm
            if (had && prevMine >= 0 && mine < 0 && inc.Players != null && inc.Players.TryGetValue(prevMine, out var gone)
                && gone.LastMove is GameAction.PlayerLeft or GameAction.PlayerKicked)
            {
                ClearTable(kicked: gone.LastMove == GameAction.PlayerKicked);
                return;
            }

            var phase = Table.Phase;
            if (phase != prevPhase)
            {
                Emit(new WorldEvent(Ev.PhaseChanged, from: prevPhase, to: phase));
                if (phase == TablePhase.Playing) Emit(prevPhase == TablePhase.RoundOver ? Ev.RoundStarted : Ev.GameStarted);
                else if (phase == TablePhase.RoundOver) Emit(Ev.RoundOver);
                else if (phase == TablePhase.GameOver) Emit(Ev.GameOver);
            }

            var turn = Table.TurnOwner ?? -1;
            if (turn != prevTurn && turn >= 0)
            {
                Emit(new WorldEvent(Ev.TurnChanged, turn));
                if (turn == mine) Emit(Ev.MyTurn);
            }

            if (inc.Action is { } a && IsMove(a))
                Emit(new WorldEvent(Ev.Move, actor, actor >= 0 ? SeatAt(actor)?.PlayerId ?? 0 : 0, a));

            // Oyuna özel yansıma: sunucu deltası elimi taşımaz, aynadaki eli delta anlamına göre düzelt
            if (!full && Table.GameId == Gin.GameId) Gin.Reflect(inc.Action ?? GameAction.None, actor == mine && mine >= 0, prevTop);

            if (full) Emit(new WorldEvent(Ev.TableSnapshot, flag: Reconciling));
            if (Table?.GameId == Gin.GameId) Gin.Validate();
        }

        /// <summary>Oyuna özel aksiyon mu (koltuk/oturum yönetimi ve Refresh değil).</summary>
        static bool IsMove(GameAction a) => a is not (GameAction.None or GameAction.Refresh or GameAction.PlayerJoined or GameAction.PlayerLeft
            or GameAction.PlayerKicked or GameAction.PlayerUpdated or GameAction.UpdatePlaytime or GameAction.Ready) && (int)a < 1000;

        static void MergeSeats(Dictionary<int, SeatPreview> cached, Dictionary<int, SeatPreview> incoming)
        {
            foreach (var (pos, inc) in incoming)
            {
                if (inc.LastMove is GameAction.PlayerLeft or GameAction.PlayerKicked) { cached.Remove(pos); continue; }
                if (!cached.TryGetValue(pos, out var c) || inc.LastMove == GameAction.PlayerJoined
                    || (inc.PlayerId.HasValue && c.PlayerId.HasValue && inc.PlayerId != c.PlayerId))
                {
                    cached[pos] = inc;
                    continue;
                }
                if (inc.PlayerId.HasValue) c.PlayerId = inc.PlayerId;
                if (inc.FacebookId.HasValue) c.FacebookId = inc.FacebookId;
                if (!string.IsNullOrEmpty(inc.Nick)) c.Nick = inc.Nick;
                if (inc.Hand != null) c.Hand = inc.Hand;
                if (inc.Balance.HasValue) c.Balance = inc.Balance;
                if (inc.Stats is { Count: > 0 }) { c.Stats ??= new(); foreach (var kv in inc.Stats) c.Stats[kv.Key] = kv.Value; }
                if (inc.LastMove.HasValue) c.LastMove = inc.LastMove;
                if (inc.Score.HasValue) c.Score = inc.Score;
                if (inc.Position != 0) c.Position = inc.Position;
                if (inc.EventResult != null) c.EventResult = inc.EventResult;
                if (!string.IsNullOrEmpty(inc.Message)) c.Message = inc.Message;
            }
        }

        static void ClearTable(bool kicked)
        {
            var had = HasTable;
            Table = null;
            Dirty(WorldPart.Table);
            TableVersion++;
            if (had) Emit(new WorldEvent(Ev.TableLeft, flag: kicked));
        }

        static void ResetTable()
        {
            Table = null; _timeSkew = 0; Reconciling = false; _left.Clear();
        }

        /// <summary>Oturum yüklemesinde sunucu "masada değilsin" dedi (CurrentTableId=0): varsa eski ayna düşer (TableLeft).</summary>
        static void DropTable()
        {
            if (Table == null) return;
            Begin(); ClearTable(kicked: false); End();
        }

        // ── istekler ─────────────────────────────────────────────────────────────
        // Hepsi oyun-bağımsız; oyun katmanı bayrak/bahis sorgusunu kendisi kurar ve buraya verir.
        public static Task<Result> PlayNowAsync(List<FlagQuery> queries = null, long? bet = null, long? minBet = null, long? maxBet = null)
            => SendTableAsync(Endpoint.TablePlayNow, GameAction.PlayNow, queries: queries, bet: bet, minBet: minBet, maxBet: maxBet, reconcile: true);
        public static Task<Result> CreateTableAsync(List<FlagQuery> queries, long? bet = null)
            => SendTableAsync(Endpoint.TableCreate, GameAction.CreateTable, queries: queries, bet: bet, reconcile: true);
        public static Task<Result> JoinAsync(int tableId) => SendTableAsync(Endpoint.TableJoin, GameAction.JoinTable, tableId: tableId, reconcile: true);
        public static Task<Result> JoinSeatAsync(int tableId, int seatIndex)
            => SendTableAsync(Endpoint.TableJoin, GameAction.JoinTable, tableId: tableId, parameters: new List<long> { seatIndex }, reconcile: true);
        /// <summary>Masayı izle (koltuk -1). Ben Spectators'ta → IsSpectator.</summary>
        public static Task<Result> SpectateAsync(int tableId) => JoinSeatAsync(tableId, -1);
        /// <summary>Koltuktan kalk → izleyici (oyun sürüyorsa koltuğa bot geçer).</summary>
        public static Task<Result> StandUpAsync() => JoinSeatAsync(Table?.TableId ?? 0, -1);
        public static Task<Result> SitDownAsync(int seatIndex) => JoinSeatAsync(Table?.TableId ?? 0, seatIndex);
        public static Task<Result> LeaveAsync() => SendTableAsync(Endpoint.TableLeave, GameAction.LeaveTable, reconcile: true);
        /// <summary>Tam görünüm iste (reconnect / tutarsızlık şüphesi).</summary>
        public static Task<Result> RefreshAsync() => SendTableAsync(Endpoint.TableAction, GameAction.Refresh, reconcile: true);
        public static Task<Result> ReadyAsync() => SendTableAsync(Endpoint.TableAction, GameAction.Ready);
        /// <summary>Oyun hamlesi (optimistik: oyun katmanı aynayı ÖNCE yamalar, burası yalnız gönderir). Sunucunun cevabı = aynı delta, push ile
        /// aynı yoldan aynaya uygulanır. Herhangi bir hata → tam görünüm iste (optimistik yama sunucuyla uyumsuz kaldı). Tek istisna
        /// InsufficientItems: kural reddi (Yüzbir'de el açmaya yetmedi), ayna doğru → Refresh gerekmez.</summary>
        public static async Task<Result> ActAsync(GameAction action, List<long> parameters = null)
        {
            var r = await SendTableAsync(Endpoint.TableAction, action, parameters: parameters);
            if (!r.Ok && r.Error != ErrorCode.InsufficientItems && HasTable) await RefreshAsync();
            return r;
        }
        public static void Act(GameAction action, List<long> parameters = null) => ActAsync(action, parameters).Forget();

        /// <summary>Masa isteği çekirdeği. <paramref name="reconcile"/> = cevap tam görünüm, bayat cache'i ezer.
        /// Hata → Ev.RequestFailed (Move=action, Text=hata).</summary>
        static async Task<Result> SendTableAsync(int endpoint, GameAction action, int? gameId = null, int? tableId = null,
            List<FlagQuery> queries = null, long? bet = null, long? minBet = null, long? maxBet = null, List<long> parameters = null,
            bool reconcile = false)
        {
            var request = new GameActionRequest
            {
                GameId = gameId ?? Game.Current.gameId,
                TableId = tableId ?? Table?.TableId,
                Action = action,
                Queries = queries, Bet = bet, MinBet = minBet, MaxBet = maxBet, Params = parameters
            };

            var result = await SendRequestAsync<GameActionResult>(endpoint, request);

            if (!result.Ok || result.Data == null) return Fail(action, result.Error ?? ErrorCode.InternalError);
            if (!result.Data.Ok) return Fail(action, result.Data.Error ?? ErrorCode.InternalError);

            Begin();
            try
            {
                if (result.Data.TableState != null) ApplyTable(result.Data.TableState, reconcile);
                else if (reconcile || action == GameAction.LeaveTable) { if (HasTable) ClearTable(kicked: false); }
            }
            finally { End(); }
            return Result.Success();
        }

        static Result Fail(GameAction action, ErrorCode error)
        {
            Emit(new WorldEvent(Ev.RequestFailed, move: action, text: error.ToString()));
            return Result.Failure(error);
        }
    }
}
