// core.upm TimeSync.cs — birebir (yalnız `using UnityEngine` kaldırıldı).
#nullable enable
using System;
using DigitoyEngine;

namespace Digitoy
{
    /// <summary>İstemci-sunucu saat eşitlemesi; istemci saat oynamasına karşı.</summary>
    public static class TimeSync
    {
        private static long _timeOffset = 0;
        private static bool _isSynced = false;
        private static long _lastSyncTime = 0;

        public static bool IsSynced => _isSynced;

        /// <summary>Saniye cinsinden ofset (serverTime - clientTime).</summary>
        public static long Offset => _timeOffset;

        /// <summary>Sunucuyla eşitlenmiş Unix zamanı (sn).</summary>
        public static long Now
        {
            get
            {
                if (!_isSynced)
                {
                    Debug.LogWarning("Not synced with server, using local time");
                    return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                var clientNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return clientNow + _timeOffset;
            }
        }

        /// <summary>PlayerState geldiğinde otomatik çağrılır.</summary>
        internal static void Sync(long serverTime)
        {
            var clientTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var newOffset = serverTime - clientTime;

            if (!_isSynced || Math.Abs(newOffset - _timeOffset) > 5)
            {
                if (_isSynced)
                    Debug.LogWarning($"Time drift detected: {newOffset - _timeOffset}s, re-syncing");

                _timeOffset = newOffset;
                _isSynced = true;
                _lastSyncTime = clientTime;
            }
        }

        internal static void Reset()
        {
            _timeOffset = 0;
            _isSynced = false;
            _lastSyncTime = 0;
        }

        public static long GetRemainingTime(long targetTimestamp) => Math.Max(0L, targetTimestamp - Now);
        public static bool HasPassed(long timestamp) => timestamp > 0 && Now >= timestamp;
        public static bool IsInFuture(long timestamp) => timestamp > Now;
        public static long ClientToServerTime(long clientTime) => clientTime + _timeOffset;
        public static long ServerToClientTime(long serverTime) => serverTime - _timeOffset;
        public static long LastSyncTime => _lastSyncTime;

        public static long SecondsSinceLastSync
        {
            get
            {
                if (!_isSynced) return 0;
                return DateTimeOffset.UtcNow.ToUnixTimeSeconds() - _lastSyncTime;
            }
        }

        public static string FormatTime(long seconds)
        {
            if (seconds <= 0) return "Ready";

            var hours = seconds / 3600;
            var minutes = (seconds % 3600) / 60;
            var secs = seconds % 60;

            if (hours > 24)
            {
                var days = hours / 24;
                hours = hours % 24;
                return $"{days}d {hours}h";
            }
            else if (hours > 0)
                return $"{hours}h {minutes}m";
            else if (minutes > 0)
                return $"{minutes}m {secs}s";
            else
                return $"{secs}s";
        }
    }
}
