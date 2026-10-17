// core.upm Components/Game/LobbyService.cs adaptasyonu: Awaitable → Task; başka fark yok.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using M;

namespace Digitoy
{
    /// <summary>Lobi/eşleşme çağrıları: online istatistik (+ push aboneliği), uygun oyuncular, masa listesi.</summary>
    public class LobbyService : Service
    {
        public static LobbyService I;
        public LobbyService() => I = this;

        /// <summary>Son alınan istatistik (istek ya da push); ilk çekimden önce / logout sonrası null.</summary>
        public OnlineStatsResponse LastStats { get; private set; }
        public bool IsSubscribed { get; private set; }
        public event Action<OnlineStatsResponse> OnOnlineStats;

        internal void HandleOnlineStats(OnlineStatsResponse stats)
        {
            LastStats = stats;
            OnOnlineStats?.Invoke(stats);
        }

        protected internal override void ResetSession()
        {
            LastStats = null;
            IsSubscribed = false;
        }

        protected internal override void ResetAll()
        {
            ResetSession();
            OnOnlineStats = null;
        }

        public async Task<Result<OnlineStatsResponse>> GetOnlineStatsAsync()
        {
            var request = new OnlineStatsRequest { BitCount = 8 };
            var result = await Server.SendRequestAsync<OnlineStatsResponse>(Endpoint.LobbyGetOnlineStats, request);
            if (result.Ok) HandleOnlineStats(result.Data);
            return result;
        }

        /// <summary>Push aboneliği; ilk istatistiği hemen döner. Oyuncu başına tek abonelik — yeni çağrı öncekini değiştirir.</summary>
        public async Task<Result<OnlineStatsResponse>> SubscribeOnlineStatsAsync(int intervalSeconds = 30, int bitCount = 0)
        {
            var request = new OnlineStatsSubscribeRequest { IntervalSeconds = intervalSeconds, BitCount = bitCount };
            var result = await Server.SendRequestAsync<OnlineStatsResponse>(Endpoint.LobbySubscribeOnlineStats, request);
            if (result.Ok)
            {
                IsSubscribed = true;
                HandleOnlineStats(result.Data);
            }
            return result;
        }

        public async Task UnsubscribeOnlineStatsAsync()
        {
            IsSubscribed = false;
            await Server.SendRequestAsync<object>(Endpoint.LobbyUnsubscribeOnlineStats, null);
        }

        public async Task<Result<List<PlayerPreview>>> GetAvailablePlayersAsync(
            Dictionary<string, StatRange> statFilters = null,
            int limit = 20)
        {
            var request = new AvailablePlayersRequest
            {
                StatFilters = statFilters,
                Limit = limit
            };
            var result = await Server.SendRequestAsync<AvailablePlayersResponse>(Endpoint.LobbyGetAvailablePlayers, request);
            if (!result.Ok)
                return Result<List<PlayerPreview>>.Failure(result.Error);
            return Result<List<PlayerPreview>>.Success(result.Data?.Players ?? new List<PlayerPreview>());
        }

        public async Task<Result<List<TablePreview>>> GetTablesAsync(
            long? flags = null,
            int? lobbyId = null,
            TablePhase? phase = null,
            int limit = 50)
        {
            var request = new TableListRequest
            {
                Flags = flags,
                BitCount = lobbyId.HasValue ? 8 : null,
                BitValue = lobbyId,
                Phase = phase,
                Limit = limit
            };
            var result = await Server.SendRequestAsync<TableListResponse>(Endpoint.LobbyGetTables, request);
            if (!result.Ok)
                return Result<List<TablePreview>>.Failure(result.Error);
            return Result<List<TablePreview>>.Success(result.Data?.Tables ?? new List<TablePreview>());
        }
    }
}
