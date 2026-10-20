// core.upm Components/Game/TableService.cs adaptasyonu: Awaitable → Task; Server.Properties.{gameId,maxSeats} →
// Game.Current.{gameId,maxSeats}. Delta merge, pozisyon dönüşümleri, zaman eğimi aynen.
// Delta sözleşmesi (docs/04-common-surface.md "Delta sözleşmesi", legacy-analysis/05 §3): null alan = değişmedi, Hand=[] = boş,
// Players kısmi → koltuk merge, boş cevap/TableId=0 = Clear, kendi Hand yalnız snapshot'ta (Join/Refresh/Start/Finish).
// Eski digiplay TableModel'in karşılığı: kablo cache'i + koltuk/sıra/timer. Oyun özeli TÜREVDE (GinTableService: el, hamle
// event'leri). View'a bildirim: tek <see cref="View"/> alanı, doğrudan metot çağrısı (eski DigiboController → TableScene); delegate yok.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy
{
    /// <summary>Masa sahnesinin servise açtığı yüz (eski DigiboController tabanı). Sahne component'i OnEnable'da
    /// <c>TableService.I.View = this</c>, OnDisable'da null. Oyun türevi bunu genişletir (IGinTableView).</summary>
    public interface ITableView
    {
        /// <summary>full = tam görünüm geldi (join/reconnect/el başı; eski refreshTable) → her eleman modelden idempotent yeniden kurulur.
        /// false = hafif: koltuk/buton/timer/faz; kartlara dokunulmaz.</summary>
        void Refresh(bool full);
        /// <summary>Ekran (relative) koltuğunda otur/kalk/atılma/profil güncellemesi.</summary>
        void SeatChanged(int seat);
        /// <summary>Masadan düştüm (ayrıldım/atıldım/oturum sıfırlandı).</summary>
        void Cleared();
    }

    /// <summary>gameId → masa servisi fabrikası. Oyunlar kendilerini <c>[ModuleInitializer]</c> ile kaydeder (Shared oyun adı bilmez,
    /// reflection yok). Kayıt yoksa düz <see cref="TableService"/>.</summary>
    public static class TableServices
    {
        static readonly Dictionary<int, Func<TableService>> Factories = new();
        public static void Register(int gameId, Func<TableService> factory) => Factories[gameId] = factory;
        public static TableService Create(int gameId) => Factories.TryGetValue(gameId, out var f) ? f() : new TableService();
    }

    public class TableService : Service
    {
        public static TableService I;
        public TableService() => I = this;
        public TableState CurrentState { get; private set; }
        public bool Exists => CurrentState?.TableId > 0;
        public bool NeedRefresh => CurrentState is not { Players.Count: > 0 };
        public int WhosTurn => RelativePosition(CurrentState?.TurnOwner ?? -1);

        /// <summary>Her uygulanan delta / clear'da artar.</summary>
        public int Version { get; private set; }
        /// <summary>Kendi Hand'imi taşıyan (= tam görünüm) her delta'da artar. Türev bu değişince eli replace eder.</summary>
        public int SnapshotCount { get; private set; }

        /// <summary>Açık masa sahnesi; yoksa null (lobi). Tek alıcı.</summary>
        public ITableView View;

        /// <summary>Masada var ama koltuğu yok (server pozisyonu -1): izleyici.</summary>
        public bool IsSpectator => Exists && GetMyPosition() == -1;

        /// <summary>Bu ekran (relative) pozisyonu ben miyim? İzleyicide ekran seat 0'da başka oyuncu görünür.</summary>
        public bool IsMe(int screenPosition) => !IsSpectator && screenPosition == 0;

        public IReadOnlyDictionary<int, SeatPreview> Spectators => CurrentState?.Spectators ?? Empty;
        static readonly Dictionary<int, SeatPreview> Empty = new();

        /// <summary>Sıra bende mi? İzleyicide her zaman false.</summary>
        public bool IsMyTurn
        {
            get
            {
                var myPos = GetMyPosition();
                return myPos >= 0 && CurrentState?.TurnOwner == myPos;
            }
        }

        public TablePhase Phase => CurrentState?.Phase ?? TablePhase.None;

        private long _timeSkew; // Sunucu - istemci zaman farkı (ms)

        public List<int> GetDiscardList(int pos = -1)
        {
            if (CurrentState?.Discards == null) return null;
            return CurrentState.Discards.TryGetValue(pos, out var list) ? list : null;
        }

        public List<int> GetDiscardListAt(int relativePosition) => GetDiscardList(DataPosition(relativePosition));
        public int GetDiscardAt(int relativePosition) => GetDiscard(DataPosition(relativePosition));

        public void ValidateAuthResult(PlayerState result)
        {
            if (result.CurrentTableId <= 0)
            {
                CurrentState = null;
                return;
            }
            // Birleştirme değil değiştirme: reconnect'te cache bayat. Çıplak state NeedRefresh'i true yapar → tam state çekilir.
            CurrentState = new TableState()
            {
                TableId = result.CurrentTableId
            };
        }

        protected internal override void ResetSession() => Clear();

        protected internal override void ResetAll()
        {
            Clear();
            View = null;
            OnError = null;
        }

        public int GetDiscard(int pos = -1)
        {
            var discardList = GetDiscardList(pos);
            if (discardList == null || discardList.Count == 0) return -1;
            return discardList[^1];
        }

        public float GetRemainingTime()
        {
            if (CurrentState?.TimeoutAt == null || CurrentState.TimeoutAt == 0)
                return -1f;
            var estimatedServerTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _timeSkew;
            var remainingMs = Math.Max(0, CurrentState.TimeoutAt.Value - estimatedServerTimeMs);
            return remainingMs / 1000f;
        }

        public float GetRemainingGameSeconds()
        {
            if (CurrentState?.GameEndsAt == null || CurrentState.GameEndsAt == 0)
                return -1f;
            var estimatedServerTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _timeSkew;
            var remainingMs = Math.Max(0, CurrentState.GameEndsAt.Value - estimatedServerTimeMs);
            return remainingMs / 1000f;
        }

        public event Action<ErrorCode> OnError;
        private int MaxSeats => Game.Current.maxSeats;

        internal void HandleServerEvent(TableState state) => Apply(state, actorFallback: -1);

        /// <summary>Bir deltanın cache'e etkisi.</summary>
        enum ApplyKind { None, Cleared, Snapshot, Delta }

        /// <summary>Tek uygulama yolu (push ve cevap): merge → Version → ekonomi sonucu → türev kancası → view.
        /// Eski digiplay: refresh(table, board) = Snapshot, updateGame(msg) = Delta.</summary>
        void Apply(TableState incoming, int actorFallback)
        {
            var kind = Merge(incoming, out var actor);
            if (kind == ApplyKind.None) return;
            Version++;

            var myPlayer = GetPlayer();
            if (myPlayer?.EventResult != null)
            {
                PlayerService.I.ApplyEventResult(myPlayer.EventResult);
                myPlayer.EventResult = null;
            }

            switch (kind)
            {
                case ApplyKind.Cleared:
                    OnCleared();
                    View?.Cleared();
                    break;
                case ApplyKind.Snapshot:
                    OnSnapshot();
                    View?.Refresh(true);
                    break;
                case ApplyKind.Delta:
                    // Önce oyun (el yaması + hamle animasyonu), sonra koltuk değişimleri, sonra hafif refresh — eski throwCard → Refresh() sırası.
                    OnDelta(incoming, actor >= 0 ? RelativePosition(actor) : actorFallback);
                    if (incoming.Players != null)
                        foreach (var (pos, seat) in incoming.Players)
                            if (seat.LastMove is GameAction.PlayerJoined or GameAction.PlayerLeft or GameAction.PlayerKicked or GameAction.PlayerUpdated)
                                View?.SeatChanged(RelativePosition(pos));
                    View?.Refresh(false);
                    break;
            }
        }

        // ── türev kancaları (eski GinTableModel.refreshBoard / updateGame) ────────────────────────────────────────
        /// <summary>Tam görünüm geldi (kendi Hand'im dahil); SnapshotCount arttı. Türev elini cache'ten replace eder.</summary>
        protected virtual void OnSnapshot() { }
        /// <summary>Parçalı delta uygulandı. <paramref name="actorSeat"/>: hamleyi yapan ekran koltuğu (-1 = bilinmiyor; cevapta 0 = ben).
        /// Türev <c>incoming.Action</c>'a göre elini yamalar ve View'a hamle metodunu çağırır. Cache zaten güncel.</summary>
        protected virtual void OnDelta(TableState incoming, int actorSeat) { }
        /// <summary>Masadan düştüm; türev kendi durumunu sıfırlar.</summary>
        protected virtual void OnCleared() { }

        /// <summary>Deltayı cache'e birleştirir. <paramref name="actor"/>: tek koltuklu deltada LastMove taşıyan server koltuğu, yoksa -1.</summary>
        private ApplyKind Merge(TableState incoming, out int actor)
        {
            actor = -1;
            if (incoming == null) return ApplyKind.None;
            var clientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (incoming.ServerTime is > 0)
                _timeSkew = incoming.ServerTime.Value - clientTimeMs;

            // Masadan düşme: {TableId=0, Action=PlayerKicked} gibi. Cache'i olduğu gibi al (Exists=false).
            if (incoming.TableId is <= 0)
            {
                Debug.Log($"TableService: TableId≤0 geldi action={incoming.Action} phase={incoming.Phase}");
                CurrentState = incoming;
                return ApplyKind.Cleared;
            }

            if (CurrentState == null)
            {
                // İlk görünüm (Join/Refresh cevabı) her zaman tam görünümdür.
                CurrentState = incoming;
                if (HasMyHand(incoming)) SnapshotCount++;
                return ApplyKind.Snapshot;
            }

            var s = CurrentState;
            if (incoming.TableId.HasValue) s.TableId = incoming.TableId;
            if (incoming.GameId.HasValue) s.GameId = incoming.GameId;
            if (incoming.TurnNo.HasValue) s.TurnNo = incoming.TurnNo;
            if (incoming.TurnOwner.HasValue) s.TurnOwner = incoming.TurnOwner;
            if (incoming.Phase != TablePhase.None) s.Phase = incoming.Phase;
            if (incoming.SubPhase != TableSubPhase.None) s.SubPhase = incoming.SubPhase;
            if (incoming.ServerTime.HasValue) s.ServerTime = incoming.ServerTime;
            if (incoming.TimeoutAt.HasValue) s.TimeoutAt = incoming.TimeoutAt;
            if (incoming.GameEndsAt.HasValue) s.GameEndsAt = incoming.GameEndsAt;
            if (incoming.Deck != null) s.Deck = incoming.Deck;
            if (incoming.DeckCount.HasValue) s.DeckCount = incoming.DeckCount;
            if (incoming.DeckLastNo.HasValue) s.DeckLastNo = incoming.DeckLastNo;
            if (incoming.Discards != null)
            {
                s.Discards ??= new Dictionary<int, List<int>>();
                foreach (var kvp in incoming.Discards)
                    s.Discards[kvp.Key] = kvp.Value;
            }

            if (incoming.Flags.HasValue) s.Flags = incoming.Flags;
            if (incoming.Bet.HasValue) s.Bet = incoming.Bet;
            if (incoming.Jackpot.HasValue) s.Jackpot = incoming.Jackpot;
            if (incoming.Action.HasValue) s.Action = incoming.Action;

            if (incoming.Spectators != null)
                MergeSeats(s.Spectators ??= new Dictionary<int, SeatPreview>(), incoming.Spectators, removeLeft: true);
            if (incoming.Players == null) return ApplyKind.Delta;
            MergeSeats(s.Players ??= new Dictionary<int, SeatPreview>(), incoming.Players);

            // Kendi Hand'im geldiyse tam görünümdür (sunucu eli yalnız Join/Refresh/Start/Finish'te yollar).
            if (HasMyHand(incoming)) { SnapshotCount++; return ApplyKind.Snapshot; }

            if (incoming.Players.Count != 1) return ApplyKind.Delta;
            foreach (var kvp in incoming.Players)
            {
                if (kvp.Value.LastMove.HasValue && kvp.Value.LastMove != GameAction.None)
                {
                    actor = kvp.Key;
                    break;
                }
            }
            return ApplyKind.Delta;
        }

        bool HasMyHand(TableState incoming)
        {
            if (incoming.Players == null) return false;
            var myId = PlayerService.I?.Id ?? 0;
            if (myId == 0) return false;
            foreach (var seat in incoming.Players.Values)
                if (seat.PlayerId == myId) return seat.Hand != null;
            return false;
        }

        /// <summary>Koltuk/izleyici delta merge'i. removeLeft: ayrılan slot silinir (izleyici); koltukta kalır (UI "ayrıldı" gösterir).</summary>
        private static void MergeSeats(Dictionary<int, SeatPreview> cached, Dictionary<int, SeatPreview> incoming, bool removeLeft = false)
        {
            foreach (var (position, incomingPlayer) in incoming)
            {
                if (removeLeft && incomingPlayer.LastMove is GameAction.PlayerLeft or GameAction.PlayerKicked)
                {
                    cached.Remove(position);
                    continue;
                }

                // Yeni katılım ya da koltukta farklı oyuncu — tamamen değiştir (bayat Stats/FacebookId kalmasın).
                if (!cached.TryGetValue(position, out var cachedPlayer) ||
                    incomingPlayer.LastMove == GameAction.PlayerJoined ||
                    (incomingPlayer.PlayerId.HasValue && cachedPlayer.PlayerId.HasValue &&
                     incomingPlayer.PlayerId != cachedPlayer.PlayerId))
                {
                    cached[position] = incomingPlayer;
                    continue;
                }

                if (incomingPlayer.PlayerId.HasValue) cachedPlayer.PlayerId = incomingPlayer.PlayerId;
                if (incomingPlayer.FacebookId.HasValue) cachedPlayer.FacebookId = incomingPlayer.FacebookId;
                if (!string.IsNullOrEmpty(incomingPlayer.Nick)) cachedPlayer.Nick = incomingPlayer.Nick;
                if (incomingPlayer.Hand != null) cachedPlayer.Hand = incomingPlayer.Hand;
                if (incomingPlayer.Balance.HasValue) cachedPlayer.Balance = incomingPlayer.Balance;

                if (incomingPlayer.Stats != null && incomingPlayer.Stats.Count > 0)
                {
                    cachedPlayer.Stats ??= new Dictionary<string, long>();
                    foreach (var kvp in incomingPlayer.Stats)
                        cachedPlayer.Stats[kvp.Key] = kvp.Value;
                }

                if (incomingPlayer.LastMove.HasValue) cachedPlayer.LastMove = incomingPlayer.LastMove;
                if (incomingPlayer.Score.HasValue) cachedPlayer.Score = incomingPlayer.Score;
                if (incomingPlayer.Position != 0) cachedPlayer.Position = incomingPlayer.Position;
                if (incomingPlayer.EventResult != null) cachedPlayer.EventResult = incomingPlayer.EventResult;
                if (!string.IsNullOrEmpty(incomingPlayer.Message)) cachedPlayer.Message = incomingPlayer.Message;
            }
        }

        /// <summary>Cache'i sessizce düşür (Refresh/Join cevabı tam görünümle hemen yeniden dolduracak; view Cleared görmez → snap yok).</summary>
        private void DropCache()
        {
            if (CurrentState != null) Version++;
            CurrentState = null;
            _timeSkew = 0;
        }

        /// <summary>Masa bitti (ayrıldım / oturum sıfırlandı): cache düşer, türev + view Cleared alır.</summary>
        private void Clear() => Clear(CurrentState != null);

        private void Clear(bool notify)
        {
            DropCache();
            if (!notify) return;
            OnCleared();
            View?.Cleared();
        }

        protected virtual Task<Result<GameActionResult>> SendGameActionRequestAsync(
            int endpoint, object request, CancellationToken cancellationToken = default) =>
            Server.SendRequestAsync<GameActionResult>(endpoint, request);

        protected virtual async Task<Result> ExecuteActionAsync(
            int endpoint,
            GameAction action,
            int? gameId = null,
            int? tableId = null,
            List<FlagQuery> queries = null,
            long? bet = null,
            long? minBet = null,
            long? maxBet = null,
            List<long> parameters = null,
            bool clearOnSuccess = false,
            bool suppressAction = false)
        {
            var request = new GameActionRequest
            {
                GameId = gameId ?? Game.Current.gameId,
                TableId = tableId ?? CurrentState?.TableId,
                Action = action,
                Queries = queries,
                Bet = bet,
                MinBet = minBet,
                MaxBet = maxBet,
                Params = parameters
            };
            var result = await SendGameActionRequestAsync(endpoint, request);

            if (!result.Ok || result.Data == null)
            {
                Debug.LogWarning($"Table {action}: {result.Error ?? ErrorCode.InternalError}");
                if (!suppressAction) OnError?.Invoke(result.Error ?? ErrorCode.InternalError);
                return Result.Failure(result.Error ?? ErrorCode.InternalError);
            }

            if (!result.Data.Ok)
            {
                Debug.LogWarning($"Table {action}: {result.Data.Error}");
                if (!suppressAction) OnError?.Invoke(result.Data.Error ?? ErrorCode.InternalError);
                return Result.Failure(result.Data.Error);
            }

            // Refresh/Join/PlayNow: bayat cache yerine cevaptaki tam görünüm. Sessiz düşür; Cleared yalnız gerçekten masasız kalınca.
            var had = CurrentState != null;
            if (clearOnSuccess)
                DropCache();

            // Cevap = kendi hamlemin deltası (sunucu BroadcastAll ile aynı paketi bana cevap, rakibe push atar); push ile aynı yoldan
            // uygulanır. Aktör belirsizse benim (ekran 0). Boş cevap = masadan çıktım (Leave) → masadaysam Cleared.
            if (result.Data.TableState != null) Apply(result.Data.TableState, actorFallback: 0);
            else Clear(had);
            return Result.Success();
        }

        public Task<Result> PlayNowAsync(List<FlagQuery> queries, long? bet = null, long? minBet = null, long? maxBet = null, bool suppressAction = false) => ExecuteActionAsync(Endpoint.TablePlayNow, GameAction.PlayNow, queries: queries, bet: bet, minBet: minBet, maxBet: maxBet, clearOnSuccess: true, suppressAction: suppressAction);
        /// <summary>Oyunun varsayılan "Play Now"u (lobi/bahis seçimi yokken). Türev oyun bayraklarını ve bahsi doldurur; taban sorgusuz dener.</summary>
        public virtual Task<Result> PlayNowAsync() => PlayNowAsync(null);
        public Task<Result> CreateAsync(List<FlagQuery> queries, long? bet = null) => ExecuteActionAsync(Endpoint.TableCreate, GameAction.CreateTable, queries: queries, bet: bet, clearOnSuccess: true);
        public Task<Result> JoinAsync(int tableId) => ExecuteActionAsync(Endpoint.TableJoin, GameAction.JoinTable, tableId: tableId, clearOnSuccess: true);
        public Task<Result> JoinWithSeatAsync(int tableId, int seatIndex) => ExecuteActionAsync(Endpoint.TableJoin, GameAction.JoinTable, tableId: tableId, clearOnSuccess: true, parameters: new List<long> { seatIndex });
        /// <summary>Masayı izle (faz kısıtı yok). Cevap tam görünüm; ben Spectators'ta → IsSpectator.</summary>
        public Task<Result> SpectateAsync(int tableId) => JoinWithSeatAsync(tableId, -1);
        /// <summary>Koltuktan kalk → izleyici. Oyun sürüyorsa koltuğa bot geçer.</summary>
        public Task<Result> StandUpAsync() => JoinWithSeatAsync(CurrentState?.TableId ?? 0, -1);
        public Task<Result> SitDownAsync(int seatIndex) => JoinWithSeatAsync(CurrentState?.TableId ?? 0, seatIndex);

        public Task<Result> LeaveAsync() => ExecuteActionAsync(Endpoint.TableLeave, GameAction.LeaveTable, clearOnSuccess: true);

        /// <summary>Hamle gönder (fire-and-forget; eski GameNetwork.sendByInstance). Oyun türevi önce kendi modelini optimistic günceller, sonra bunu çağırır.
        /// Cevap (= echo delta) push ile aynı yoldan uygulanır; hata → OnError + <see cref="RefreshAsync"/> (öz-iyileşme).</summary>
        protected void Send(GameAction action, List<long> parameters = null) => SendAsync(action, parameters).Forget();

        async Task SendAsync(GameAction action, List<long> parameters)
        {
            var r = await ExecuteActionAsync(Endpoint.TableAction, action, parameters: parameters);
            if (!r.Ok && Exists) await RefreshAsync();
        }
        public Task<Result> Refresh() => ExecuteActionAsync(Endpoint.TableAction, GameAction.Refresh, clearOnSuccess: true);
        public Task<Result> RefreshAsync() => ExecuteActionAsync(Endpoint.TableAction, GameAction.Refresh, clearOnSuccess: true);

        public void Action(GameAction action, List<long> parameters = null) => ExecuteActionAsync(Endpoint.TableAction, action, parameters: parameters).Forget();
        public Task<Result> ActionAsync(GameAction action, List<long> parameters = null) => ExecuteActionAsync(Endpoint.TableAction, action, parameters: parameters);

        /// <summary>Ekran (relative) pozisyonu: 0 = ben. "Ben" semantiği taşır: izleyicide null. Görüntü için GetSeatAt.</summary>
        public SeatPreview GetPlayer(int relativePosition = 0)
        {
            if (CurrentState?.Players == null)
                return null;
            var myServerPosition = GetMyPosition();
            if (myServerPosition == -1)
                return null;
            if (relativePosition < 0 || relativePosition >= MaxSeats)
                return null;
            var pos = (relativePosition + myServerPosition) % MaxSeats;
            return CurrentState.Players.GetValueOrDefault(pos);
        }

        public int GetMyPosition()
        {
            if (CurrentState?.Players == null)
                return -1;

            var myPlayerId = PlayerService.I.Id;
            if (myPlayerId == 0)
                return -1;

            foreach (var kvp in CurrentState.Players)
            {
                if (kvp.Value.PlayerId == myPlayerId)
                    return kvp.Key;
            }

            return -1;
        }

        /// <summary>Ekran pozisyonundaki koltuğu görüntü amaçlı okur; izleyicide seat-0 perspektifi.</summary>
        public SeatPreview GetSeatAt(int relativePosition) =>
            CurrentState?.Players?.GetValueOrDefault(DataPosition(relativePosition));

        /// <summary>Data (server) pozisyonu → ekran (relative) pozisyonu. serverPosition &lt; 0 → -1. İzleyicide identity.</summary>
        public int RelativePosition(int serverPosition)
        {
            if (serverPosition < 0) return -1;
            var myServerPosition = Math.Max(0, GetMyPosition());
            var shift = MaxSeats - myServerPosition;
            var pos = (serverPosition + shift) % MaxSeats;
            return pos;
        }

        /// <summary>Ekran (relative) → data (server) pozisyonu. İzleyicide identity.</summary>
        public int DataPosition(int relativePosition)
        {
            var myServerPosition = Math.Max(0, GetMyPosition());
            return (relativePosition + myServerPosition) % MaxSeats;
        }
    }
}
