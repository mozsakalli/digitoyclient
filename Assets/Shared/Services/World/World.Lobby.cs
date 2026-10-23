// World.Lobby: online istatistik aynası (istek cevabı ya da abonelik push'u aynı kapıdan) + lobi istekleri (masa/oyuncu listeleri
// modele yazılmaz; anlık sorgu sonucu döner).
using System.Collections.Generic;
using System.Threading.Tasks;
using M;

namespace Digitoy
{
    public static partial class World
    {
        public static OnlineStatsResponse Online { get; private set; }
        public static bool OnlineSubscribed { get; private set; }

        static void ApplyOnline(OnlineStatsResponse o)
        {
            if (o == null) return;
            Online = o;
            Dirty(WorldPart.Online);
            Emit(Ev.OnlineStatsChanged);
        }

        static void ResetLobby() { Online = null; OnlineSubscribed = false; }

        // ── istekler ──
        public static async Task<Result<OnlineStatsResponse>> GetOnlineStatsAsync()
        {
            var result = await SendRequestAsync<OnlineStatsResponse>(Endpoint.LobbyGetOnlineStats, new OnlineStatsRequest { BitCount = 8 });
            if (result.Ok) Apply(result.Data);
            return result;
        }

        /// <summary>Push aboneliği; ilk istatistik hemen döner. Oyuncu başına tek abonelik — yeni çağrı öncekini değiştirir.</summary>
        public static async Task<Result<OnlineStatsResponse>> SubscribeOnlineStatsAsync(int intervalSeconds = 30, int bitCount = 0)
        {
            var request = new OnlineStatsSubscribeRequest { IntervalSeconds = intervalSeconds, BitCount = bitCount };
            var result = await SendRequestAsync<OnlineStatsResponse>(Endpoint.LobbySubscribeOnlineStats, request);
            if (result.Ok) { OnlineSubscribed = true; Apply(result.Data); }
            return result;
        }

        public static async Task UnsubscribeOnlineStatsAsync()
        {
            OnlineSubscribed = false;
            await SendRequestAsync<object>(Endpoint.LobbyUnsubscribeOnlineStats, null);
        }

        public static async Task<Result<List<PlayerPreview>>> GetAvailablePlayersAsync(Dictionary<string, StatRange> statFilters = null, int limit = 20)
        {
            var result = await SendRequestAsync<AvailablePlayersResponse>(Endpoint.LobbyGetAvailablePlayers,
                new AvailablePlayersRequest { StatFilters = statFilters, Limit = limit });
            return result.Ok
                ? Result<List<PlayerPreview>>.Success(result.Data?.Players ?? new List<PlayerPreview>())
                : Result<List<PlayerPreview>>.Failure(result.Error);
        }

        public static async Task<Result<List<TablePreview>>> GetTablesAsync(long? flags = null, int? lobbyId = null, TablePhase? phase = null, int limit = 50)
        {
            var request = new TableListRequest
            {
                Flags = flags,
                BitCount = lobbyId.HasValue ? 8 : null,
                BitValue = lobbyId,
                Phase = phase,
                Limit = limit
            };
            var result = await SendRequestAsync<TableListResponse>(Endpoint.LobbyGetTables, request);
            return result.Ok
                ? Result<List<TablePreview>>.Success(result.Data?.Tables ?? new List<TablePreview>())
                : Result<List<TablePreview>>.Failure(result.Error);
        }
    }
}
