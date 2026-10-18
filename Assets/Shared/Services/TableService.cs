// core.upm Components/Game/TableService.cs adaptasyonu: Awaitable → Task; Server.Properties.{gameId,maxSeats} →
// Game.Current.{gameId,maxSeats}. Delta merge, pozisyon dönüşümleri, zaman eğimi aynen.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using M;

namespace Digitoy
{
    public class TableService : Service
    {
        public static TableService I;
        public TableService() => I = this;
        public TableState CurrentState { get; private set; }
        public bool Exists => CurrentState?.TableId > 0;
        public bool NeedRefresh => CurrentState is not { Players.Count: > 0 };
        public int WhosTurn => RelativePosition(CurrentState?.TurnOwner ?? -1);

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
            OnUpdate = null;
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

        public event Action<int> OnUpdate;
        public event Action<ErrorCode> OnError;
        private int MaxSeats => Game.Current.maxSeats;

        internal void HandleServerEvent(TableState state)
        {
            var a = UpdateState(state);
            var myPlayer = GetPlayer();
            if (myPlayer?.EventResult != null)
                PlayerService.I.ApplyEventResult(myPlayer.EventResult);
            var whodid = RelativePosition(a);
            OnUpdate?.Invoke(whodid);
            if (myPlayer != null) myPlayer.EventResult = null;
        }

        private int UpdateState(TableState incoming)
        {
            if (incoming == null) return -1;
            var clientTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (CurrentState == null)
            {
                CurrentState = incoming;
                if (incoming.ServerTime is not > 0) return -1;
                _timeSkew = incoming.ServerTime.Value - clientTimeMs;
                return -1;
            }

            if (incoming.ServerTime is > 0)
                _timeSkew = incoming.ServerTime.Value - clientTimeMs;

            if (incoming.TableId is <= 0)
            {
                CurrentState = incoming;
                return 0;
            }

            if (incoming.TableId.HasValue) CurrentState.TableId = incoming.TableId;
            if (incoming.GameId.HasValue) CurrentState.GameId = incoming.GameId;
            if (incoming.TurnNo.HasValue) CurrentState.TurnNo = incoming.TurnNo;
            if (incoming.TurnOwner.HasValue) CurrentState.TurnOwner = incoming.TurnOwner;
            if (incoming.Phase != TablePhase.None) CurrentState.Phase = incoming.Phase;
            if (incoming.SubPhase != TableSubPhase.None) CurrentState.SubPhase = incoming.SubPhase;
            if (incoming.ServerTime.HasValue) CurrentState.ServerTime = incoming.ServerTime;
            if (incoming.TimeoutAt.HasValue) CurrentState.TimeoutAt = incoming.TimeoutAt;
            if (incoming.GameEndsAt.HasValue) CurrentState.GameEndsAt = incoming.GameEndsAt;
            if (incoming.Deck != null) CurrentState.Deck = incoming.Deck;
            if (incoming.DeckCount.HasValue) CurrentState.DeckCount = incoming.DeckCount;
            if (incoming.DeckLastNo.HasValue) CurrentState.DeckLastNo = incoming.DeckLastNo;
            if (incoming.Discards != null)
            {
                CurrentState.Discards ??= new Dictionary<int, List<int>>();
                foreach (var kvp in incoming.Discards)
                    CurrentState.Discards[kvp.Key] = kvp.Value;
            }

            if (incoming.Flags.HasValue) CurrentState.Flags = incoming.Flags;
            if (incoming.Bet.HasValue) CurrentState.Bet = incoming.Bet;
            if (incoming.Jackpot.HasValue) CurrentState.Jackpot = incoming.Jackpot;
            if (incoming.Action.HasValue) CurrentState.Action = incoming.Action;

            if (incoming.Spectators != null) MergeSeats(CurrentState.Spectators ??= new Dictionary<int, SeatPreview>(), incoming.Spectators, removeLeft: true);
            if (incoming.Players == null) return -1;
            MergeSeats(CurrentState.Players ??= new Dictionary<int, SeatPreview>(), incoming.Players);

            var whodid = -1;
            if (incoming.Players.Count != 1) return whodid;
            foreach (var kvp in incoming.Players)
            {
                if (kvp.Value.LastMove.HasValue && kvp.Value.LastMove != GameAction.None)
                {
                    whodid = kvp.Key;
                    break;
                }
            }

            return whodid;
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

        private void Clear()
        {
            CurrentState = null;
            _timeSkew = 0;
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
                if (!suppressAction) OnError?.Invoke(result.Error ?? ErrorCode.InternalError);
                return Result.Failure(result.Error ?? ErrorCode.InternalError);
            }

            if (!result.Data.Ok)
            {
                if (!suppressAction) OnError?.Invoke(result.Data.Error ?? ErrorCode.InternalError);
                return Result.Failure(result.Data.Error);
            }

            if (clearOnSuccess)
                Clear();

            if (result.Data.TableState != null)
            {
                UpdateState(result.Data.TableState);

                // Çağıranın ekonomi event sonucu push'la değil aksiyon cevabıyla gelir.
                var myPlayer = GetPlayer();
                if (myPlayer?.EventResult != null)
                {
                    PlayerService.I.ApplyEventResult(myPlayer.EventResult);
                    myPlayer.EventResult = null;
                }
            }
            else Clear();
            if (!suppressAction) OnUpdate?.Invoke(0);
            return Result.Success();
        }

        public Task<Result> PlayNowAsync(List<FlagQuery> queries, long? bet = null, long? minBet = null, long? maxBet = null, bool suppressAction = false) => ExecuteActionAsync(Endpoint.TablePlayNow, GameAction.PlayNow, queries: queries, bet: bet, minBet: minBet, maxBet: maxBet, clearOnSuccess: true, suppressAction: suppressAction);
        public Task<Result> CreateAsync(List<FlagQuery> queries, long? bet = null) => ExecuteActionAsync(Endpoint.TableCreate, GameAction.CreateTable, queries: queries, bet: bet, clearOnSuccess: true);
        public Task<Result> JoinAsync(int tableId) => ExecuteActionAsync(Endpoint.TableJoin, GameAction.JoinTable, tableId: tableId, clearOnSuccess: true);
        public Task<Result> JoinWithSeatAsync(int tableId, int seatIndex) => ExecuteActionAsync(Endpoint.TableJoin, GameAction.JoinTable, tableId: tableId, clearOnSuccess: true, parameters: new List<long> { seatIndex });
        /// <summary>Masayı izle (faz kısıtı yok). Cevap tam görünüm; ben Spectators'ta → IsSpectator.</summary>
        public Task<Result> SpectateAsync(int tableId) => JoinWithSeatAsync(tableId, -1);
        /// <summary>Koltuktan kalk → izleyici. Oyun sürüyorsa koltuğa bot geçer.</summary>
        public Task<Result> StandUpAsync() => JoinWithSeatAsync(CurrentState?.TableId ?? 0, -1);
        public Task<Result> SitDownAsync(int seatIndex) => JoinWithSeatAsync(CurrentState?.TableId ?? 0, seatIndex);

        public Task<Result> LeaveAsync() => ExecuteActionAsync(Endpoint.TableLeave, GameAction.LeaveTable, clearOnSuccess: true);

        public void PickFromDeck()
        {
            var parameters = new List<long> { CurrentState?.DeckLastNo ?? -1 };
            ExecuteActionAsync(Endpoint.TableAction, GameAction.PickFromDeck, parameters: parameters).Forget();
        }

        public void PickFromDiscard(int no) => PickAsync(no, GameAction.PickFromDiscard).Forget();
        public Task<Result> PickFromDiscardAsync(int no) => PickAsync(no, GameAction.PickFromDiscard);
        public void PickFromDeck(int no) => PickAsync(no, GameAction.PickFromDeck).Forget();
        public Task<Result> PickFromDeckAsync(int no) => PickAsync(no, GameAction.PickFromDeck);
        public Task<Result> PickFromDeckAsync() => PickAsync(CurrentState?.DeckLastNo ?? -1, GameAction.PickFromDeck);

        private async Task<Result> PickAsync(int no, GameAction action)
        {
            var result = await ExecuteActionAsync(Endpoint.TableAction, action, parameters: new List<long> { no });
            if (result.Ok)
                GetPlayer()?.Hand?.Add(no);
            return result;
        }

        public void Discard(int no) => DiscardAsync(no).Forget();

        public async Task<Result> DiscardAsync(int no)
        {
            var parameters = new List<long> { no };
            var result = await ExecuteActionAsync(Endpoint.TableAction, GameAction.Discard, parameters: parameters);
            if (result.Ok)
                GetPlayer()?.Hand?.Remove(no);
            return result;
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
