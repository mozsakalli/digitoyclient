// World.Player: PlayerState aynası + EventExecutionResult uygulaması (envanter Δ, event state, leaderboard) + zaman eşikleri.
// PlayerState her gelişte KOMPLE değişir (login/reconnect); eskisi varsa envanter farkı → InventoryChanged (reconnect'te
// kaçırılan ödüller görünür). EventExecutionResult: eski PlayerService.ApplyEventResult kuralı birebir (Amount toplanır, diğerleri yazılır).
// Tick: EventState.NextAt geçti → EventAvailable (bir kez, NextAt değişene kadar); InventoryItem.ResetTime geçti → InventoryReset.
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy
{
    public static partial class World
    {
        public static PlayerState Player { get; private set; }
        /// <summary>Auth anındaki PlayerId; PlayerState gelince ondan okunur.</summary>
        public static long MyId => Player?.PlayerId ?? _authId;
        static long _authId;
        static readonly HashSet<string> _announcedAvailable = new();

        internal static void SetAuthId(long id) { if (id > 0) _authId = id; }

        public static string Nick => string.IsNullOrEmpty(Player?.Nick) ? $"M{MyId}" : Player.Nick;
        public static bool IsNickSet => !string.IsNullOrEmpty(Player?.Nick);
        public static bool HasFacebookConnection => Player?.FacebookId > 0;
        public static bool HasAppleConnection => !string.IsNullOrEmpty(Player?.AppleId);
        public static bool HasSocialConnection => HasFacebookConnection || HasAppleConnection;
        public static bool IsScheduledForDeletion => Player?.DeletionScheduledAt > 0;
        /// <summary>Sunucuya göre oturduğum masa (oturum yüklemesinde kullanılır; canlı ayna Table).</summary>
        public static int CurrentTableId => Player?.CurrentTableId ?? 0;

        public static long Amount(string itemId) => Item(itemId)?.EffectiveAmount ?? 0;
        public static bool Has(string itemId) => Player?.Inventory?.ContainsKey(itemId) ?? false;
        public static InventoryItem Item(string itemId) => Player?.Inventory != null && Player.Inventory.TryGetValue(itemId, out var i) ? i : null;
        public static EventState Event(string eventId) => Player?.EventStates != null && Player.EventStates.TryGetValue(eventId, out var e) ? e : null;
        /// <summary>Event tekrar alınabilir mi: NextAt yok ya da geçti.</summary>
        public static bool IsEventAvailable(string eventId) { var e = Event(eventId); return e == null || e.NextAt <= 0 || e.NextAt <= TimeSync.Now; }

        // ── istekler ──
        /// <summary>PlayerState'i çek ve uygula (login / reconnect).</summary>
        public static async Task<Result> LoadPlayerAsync()
        {
            var result = await SendRequestAsync<PlayerState>(Endpoint.PlayerGetState, null);
            if (!result.Ok) return Result.Failure(result.Error);
            Debug.Log($"Player state loaded | PlayerId={result.Data.PlayerId} | ServerTime={result.Data.ServerTime} | Table={result.Data.CurrentTableId} | Nick={result.Data.Nick}");
            Apply(result.Data);
            return Result.Success();
        }

        /// <summary>Profil Önizlemesi (0 = kendim). Modele yazılmaz; arkadaşa ApplyPreview ile de uygulanır.</summary>
        public static async Task<Result<PlayerPreview>> GetProfileAsync(long playerId = 0, CancellationToken ct = default)
        {
            var result = await SendRequestAsync<PlayerPreview>(Endpoint.PlayerGetProfile, new ProfileRequest { PlayerId = playerId }, ct);
            return result.Ok ? Result<PlayerPreview>.Success(result.Data) : Result<PlayerPreview>.Failure(result.Error);
        }

        static void ApplyPlayer(PlayerState p)
        {
            var prev = Player;
            Player = p;
            TimeSync.Sync(p.ServerTime);
            _announcedAvailable.Clear();
            Dirty(WorldPart.Player);
            Emit(Ev.PlayerLoaded);
            if (prev?.Inventory == null || p.Inventory == null) return;
            foreach (var (id, item) in p.Inventory)
            {
                var old = prev.Inventory.TryGetValue(id, out var o) ? o.Amount : 0;
                if (item.Amount != old) Emit(new WorldEvent(Ev.InventoryChanged, id: id, amount: item.Amount - old));
            }
            // eskide olup yenide olmayan eşya → sıfıra düştü
            foreach (var (id, o) in prev.Inventory)
                if (o.Amount != 0 && !p.Inventory.ContainsKey(id)) Emit(new WorldEvent(Ev.InventoryChanged, id: id, amount: -o.Amount));
        }

        static void ApplyEventResult(EventExecutionResult r)
        {
            if (r == null || Player == null || !r.Ok) return;
            Dirty(WorldPart.Player);
            if (r.InventoryUpdates != null)
            {
                Player.Inventory ??= new();
                foreach (var (id, u) in r.InventoryUpdates)
                {
                    if (!Player.Inventory.TryGetValue(id, out var item)) Player.Inventory[id] = item = new InventoryItem();
                    if (u.Amount.HasValue) item.Amount += u.Amount.Value;
                    if (u.Rate.HasValue) item.Rate = u.Rate.Value;
                    if (u.Time.HasValue) item.Time = u.Time.Value;
                    if (u.ResetTime.HasValue) item.ResetTime = u.ResetTime.Value;
                }
            }
            if (r.EventStateUpdates != null)
            {
                Player.EventStates ??= new();
                foreach (var (id, s) in r.EventStateUpdates) { Player.EventStates[id] = s; _announcedAvailable.Remove(id); }
            }
            if (r.LeaderboardUpdates != null)
            {
                Player.LeaderboardStates ??= new();
                foreach (var (id, s) in r.LeaderboardUpdates) Player.LeaderboardStates[id] = s;
            }

            // model yazıldı → olaylar
            if (!string.IsNullOrEmpty(r.EventId)) Emit(new WorldEvent(Ev.EventApplied, id: r.EventId));
            if (r.InventoryUpdates != null)
                foreach (var (id, u) in r.InventoryUpdates)
                    if (u.Amount is { } d && d != 0) Emit(new WorldEvent(Ev.InventoryChanged, id: id, amount: d));
            if (r.EventStateUpdates != null)
                foreach (var id in r.EventStateUpdates.Keys) Emit(new WorldEvent(Ev.EventProgress, id: id));
            if (r.LeaderboardUpdates != null)
                foreach (var id in r.LeaderboardUpdates.Keys) Emit(new WorldEvent(Ev.LeaderboardChanged, id: id));
        }

        static void TickPlayer()
        {
            if (Player == null) return;
            var now = TimeSync.Now;
            if (Player.EventStates != null)
                foreach (var (id, e) in Player.EventStates)
                {
                    if (e.NextAt <= 0 || e.NextAt > now || _announcedAvailable.Contains(id)) continue;
                    _announcedAvailable.Add(id);
                    Dirty(WorldPart.Player);
                    Emit(new WorldEvent(Ev.EventAvailable, id: id));
                }
            if (Player.Inventory != null)
                foreach (var (id, item) in Player.Inventory)
                {
                    if (item.ResetTime <= 0 || item.ResetTime > now) continue;
                    item.Amount = 0; item.ResetTime = 0;
                    Dirty(WorldPart.Player);
                    Emit(new WorldEvent(Ev.InventoryReset, id: id));
                }
        }

        static void ResetPlayer() { Player = null; _authId = 0; _announcedAvailable.Clear(); }
    }
}
