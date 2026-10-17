// core.upm Network/INetworkClient.cs — birebir (`using UnityEngine` kaldırıldı).
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using M;

namespace Digitoy
{
    internal interface INetworkClient
    {
        bool IsConnected { get; }
        int LatencyMs { get; }
        string ConnectedUrl { get; }
        Task<ErrorCode?> ConnectAsync(CancellationToken cancellationToken = default, bool relogin = false);
        void Disconnect(CloseReason reason = CloseReason.Normal);
        Task<T?> SendRequestAsync<T>(int endpoint, object? request, CancellationToken cancellationToken = default) where T : class;
        event Action<CloseReason> OnDisconnected;
        event Action<object> OnEvent;
    }
}
