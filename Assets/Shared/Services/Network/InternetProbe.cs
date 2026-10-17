// core.upm Network/InternetProbe.cs adaptasyonu. Fark: UnityWebRequest → motor Http.GetAsync; Application.internet-
// Reachability hızlı yolu yok (motor API'si yok); yönlendirme takibi motorun davranışına bağlı — 3xx görülmezse
// gövde kontrolü captive portal'ı yakalar. Zaman aşımı motor Wait.Timeout ile (tek-thread).
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;

namespace Digitoy.Network
{
    public enum InternetStatus
    {
        Online,
        Offline,
        CaptivePortal
    }

    /// <summary>Gerçek internet erişilebilirlik probu: bilinen captive-portal uçlarına paralel küçük istekler.</summary>
    public static class InternetProbe
    {
        private static readonly string[] Probes =
        {
            "https://www.gstatic.com/generate_204",
            "https://captive.apple.com/hotspot-detect.html",
            "https://1.1.1.1/cdn-cgi/trace",
        };

        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

        public static async Task<InternetStatus> CheckAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            var perProbeTimeout = timeout ?? DefaultTimeout;

            var tasks = new Task<InternetStatus>[Probes.Length];
            for (var i = 0; i < Probes.Length; i++)
                tasks[i] = ProbeOneAsync(Probes[i], perProbeTimeout, ct);

            var sawCaptive = false;
            var remaining = new List<Task<InternetStatus>>(tasks);
            while (remaining.Count > 0)
            {
                var done = await Task.WhenAny(remaining);
                remaining.Remove(done);

                var result = await done;
                if (result == InternetStatus.Online) return InternetStatus.Online;
                if (result == InternetStatus.CaptivePortal) sawCaptive = true;
            }

            return sawCaptive ? InternetStatus.CaptivePortal : InternetStatus.Offline;
        }

        /// <summary>Yalnız gerçek internet varsa true; hiç fırlatmaz.</summary>
        public static async Task<bool> HasInternetAsync(TimeSpan? timeout = null, CancellationToken ct = default)
        {
            try
            {
                var status = await CheckAsync(timeout, ct);
                return status == InternetStatus.Online;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<InternetStatus> ProbeOneAsync(string url, TimeSpan timeout, CancellationToken ct)
        {
            try
            {
                using var timeoutCts = Wait.Timeout((float)timeout.TotalSeconds);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                var resp = await Http.GetAsync(url, linked.Token);
                if (linked.IsCancellationRequested) return InternetStatus.Offline;

                var code = resp.Status;
                if (code >= 300 && code < 400) return InternetStatus.CaptivePortal;
                if (!resp.Ok) return InternetStatus.Offline;
                if (code == 204) return InternetStatus.Online;
                if (code != 200) return InternetStatus.Offline;

                if (url.Contains("captive.apple.com"))
                {
                    var body = resp.Text ?? string.Empty;
                    return body.Contains("Success") ? InternetStatus.Online : InternetStatus.CaptivePortal;
                }

                return InternetStatus.Online;
            }
            catch
            {
                return InternetStatus.Offline;
            }
        }
    }
}
