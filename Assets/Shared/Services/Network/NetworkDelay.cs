// core.upm Network/NetworkDelay.cs adaptasyonu. Fark: Task.Delay (thread-pool timer, ana thread dışı devam) yerine
// motor Wait.Seconds (frame tabanlı, tek-thread). Davranış: iptal → OperationCanceledException (orijinalle aynı).
using System;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;

namespace Digitoy.Network
{
    internal static class NetworkDelay
    {
        internal static async Task WaitAsync(TimeSpan delay, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Wait.Seconds((float)delay.TotalSeconds, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal static async Task CancelAfterAsync(
            CancellationTokenSource source, TimeSpan delay, CancellationToken lifetime)
        {
            try
            {
                await WaitAsync(delay, lifetime);
                source.Cancel();
            }
            catch (OperationCanceledException)
            {
                // İşlem zaman aşımından önce bitti. (AOT frontend'de exception filter `when` yok → koşul gövdede.)
                if (!lifetime.IsCancellationRequested) throw;
            }
        }
    }
}
