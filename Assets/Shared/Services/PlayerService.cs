// core.upm Components/Player/PlayerService.cs adaptasyonu (Faz 0 alt kümesi). Fark: Awaitable → Task; ekonomi/level/
// leaderboard/avatar/nick/deletion/messaging bağımlılıkları (Events, EventKeys, ItemKeys, LeaderboardService, MessagingService,
// ProfileData) Faz 5'te core.upm'den gelir. Kimlik, token, state, inventory ve ApplyEventResult aynen.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using M;
using DigitoyEngine;

namespace Digitoy
{
    public class PlayerService : Service
    {
        public static PlayerService I = null!;
        public PlayerService() => I = this;

        private PlayerState? _state;

        internal Dictionary<long, Friendship>? Friends => _state?.Friends;

        public event Action? OnUpdate;

        // Auth anında gelen PlayerId; full state (GetStateAsync) yüklenene kadar kimlik kaynağı.
        private long _authId;
        public long Id => _state?.PlayerId ?? _authId;

        internal void ValidateAuthResult(AuthResult result)
        {
            if (result.PlayerId > 0) _authId = result.PlayerId;
        }

        public long FacebookId => _state?.FacebookId ?? -1;
        public string? AppleId => _state?.AppleId;
        public bool HasFacebookConnection => _state?.FacebookId > 0;
        public bool HasAppleConnection => !string.IsNullOrEmpty(_state?.AppleId);
        public bool HasSocialConnection => HasFacebookConnection || HasAppleConnection;

        public bool HasToken => LocalCache.HasToken;
        public string? GetToken() => LocalCache.GetToken();
        internal void SaveToken(string token) => LocalCache.SaveToken(token);
        public string Nick => IsNickSet ? _state?.Nick ?? "N/A" : $"M{_state?.PlayerId}";
        public bool IsNickSet => !string.IsNullOrEmpty(_state?.Nick);

        public bool IsScheduledForDeletion => _state?.DeletionScheduledAt > 0;
        public string Referral => _state?.ReferralCode ?? "N/A";
        public int CurrentTableId => _state?.CurrentTableId ?? 0;

        public long Get(string itemId) => GetInventoryItem(itemId)?.EffectiveAmount ?? 0;
        public bool Has(string itemId) => _state?.Inventory.ContainsKey(itemId) ?? false;
        public bool IsInitialized => _state != null;

        internal InventoryItem? GetInventoryItem(string itemId)
        {
            if (_state?.Inventory == null || !_state.Inventory.TryGetValue(itemId, out var item)) return null;
            if (item is not { ResetTime: > 0 } || item.ResetTime > TimeSync.Now) return item;
            // Süresi dolan dönemsel eşya: sıfırla
            item.Amount = 0;
            item.ResetTime = 0;
            return item;
        }

        public async Task<Result> GetStateAsync()
        {
            var result = await Server.SendRequestAsync<PlayerState>(Endpoint.PlayerGetState, null);

            if (!result.Ok)
                return Result.Failure(result.Error);

            TableService.I.ValidateAuthResult(result.Data);

            Debug.Log($"Player state loaded | PlayerId={result.Data.PlayerId} | ServerTime={result.Data.ServerTime} | Table={result.Data.CurrentTableId} | Nick={result.Data.Nick}");
            ResetSession();
            _state = result.Data;
            TimeSync.Sync(result.Data.ServerTime);

            OnPlayerUpdated(state: true);
            return Result.Success();
        }

        public async Task<Result<PlayerPreview>> GetPreviewAsync(long targetPlayerId = 0, CancellationToken ct = default)
        {
            var request = new ProfileRequest { PlayerId = targetPlayerId };
            var result = await Server.SendRequestAsync<PlayerPreview>(Endpoint.PlayerGetProfile, request, ct);

            return result.Ok
                ? Result<PlayerPreview>.Success(result.Data)
                : Result<PlayerPreview>.Failure(result.Error);
        }

        internal void ApplyEventResult(EventExecutionResult? result)
        {
            if (result == null || _state == null) return;
            var changed = false;
            if (result.InventoryUpdates != null)
            {
                foreach (var (itemId, update) in result.InventoryUpdates)
                {
                    if (!_state.Inventory.TryGetValue(itemId, out var item))
                    {
                        item = new InventoryItem { Amount = 0, Rate = 0, Time = 0, ResetTime = 0 };
                        _state.Inventory[itemId] = item;
                    }

                    if (update.Amount.HasValue) item.Amount += update.Amount.Value;
                    if (update.Rate.HasValue) item.Rate = update.Rate.Value;
                    if (update.Time.HasValue) item.Time = update.Time.Value;
                    if (update.ResetTime.HasValue) item.ResetTime = update.ResetTime.Value;
                }

                changed = true;
            }

            if (result.EventStateUpdates != null)
            {
                foreach (var (eventId, eventState) in result.EventStateUpdates)
                    _state.EventStates[eventId] = eventState;
                changed = true;
            }

            if (result.LeaderboardUpdates != null)
            {
                foreach (var (leaderboardId, leaderboardState) in result.LeaderboardUpdates)
                    _state.LeaderboardStates[leaderboardId] = leaderboardState;
                changed = true;
            }

            if (changed) OnPlayerUpdated(changes: result);
        }

        protected internal override void ResetSession()
        {
            _state = null;
            _authId = 0;
            TimeSync.Reset();
        }

        protected internal override void ResetAll()
        {
            ResetSession();
            OnUpdate = null;
        }

        protected virtual void OnPlayerUpdated(EventExecutionResult? changes = null, bool state = false, bool nick = false)
        {
            OnUpdate?.Invoke();
        }
    }
}
