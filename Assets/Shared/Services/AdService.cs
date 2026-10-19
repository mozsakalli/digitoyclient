// Reklam servisi: oyun kodu mediation SDK'sini bilmez (AppLovin tipi yalniz burada). Init AppBoot'ta; rewarded/interstitial
// gosterimi Task doner. Host yoksa (editor mock'suz, Windows) her sey "unavailable" = false; oyun akisi bozulmaz.
using System.Threading.Tasks;
using DigitoyEngine;

namespace Digitoy
{
    public sealed class AdService : Service
    {
        public static AdService I;
        public AdService() => I = this;

        AppLovin _ads;
        public bool Available => _ads != null && _ads.Available;
        public bool Initialized => _ads != null && _ads.Initialized;
        public bool RewardedReady => _ads != null && _ads.Rewarded.IsReady;
        public bool InterstitialReady => _ads != null && _ads.Interstitial.IsReady;
        public double SessionRevenue { get; private set; }

        public Task<bool> Init()
        {
            _ads ??= Plugin.Get<AppLovin>();
            if (!_ads.Available) return Task.FromResult(false);
            _ads.OnRevenue -= OnRevenue;
            _ads.OnRevenue += OnRevenue;
            _ads.OnLoaded -= OnLoaded;
            _ads.OnLoaded += OnLoaded;
            return _ads.Init();
        }

        static void OnLoaded(AppLovin.FullscreenAd ad) => Debug.Log("[ads] loaded " + ad.Unit + " (" + ad.Network + ")");

        // GDPR/CCPA: CMP sonucu ya da politika akisi; init'ten once de cagrilabilir.
        public void SetPrivacy(bool hasUserConsent, bool doNotSell = false) => _ads?.SetPrivacy(hasUserConsent, doNotSell);

        /// <summary>Rewarded gosterir; true = odul verilecek (kullanici sonuna kadar izledi).</summary>
        public async Task<bool> ShowRewarded(string placement)
        {
            if (!Available) return false;
            var r = await _ads.Rewarded.Show(placement);
            if (r.Status != AppLovin.ShowStatus.Rewarded && r.Status != AppLovin.ShowStatus.Shown)
                Debug.Log("[ads] rewarded " + placement + ": " + r.Status + (r.Error != null ? " " + r.Error : ""));
            return r.Status == AppLovin.ShowStatus.Rewarded;
        }

        /// <summary>Interstitial gosterir; true = gosterildi (hazir degilse false, yukleme tetiklenir).</summary>
        public async Task<bool> ShowInterstitial(string placement)
        {
            if (!Available) return false;
            var r = await _ads.Interstitial.Show(placement);
            return r.Status == AppLovin.ShowStatus.Shown || r.Status == AppLovin.ShowStatus.Rewarded;
        }

        public void ShowMediationDebugger() => _ads?.ShowMediationDebugger();

        void OnRevenue(AppLovin.RevenueEvent e)
        {
            SessionRevenue += e.Revenue;
            Debug.Log("[ads] revenue " + e.Unit + " " + e.Network + " " + e.Revenue);
        }

        protected internal override void ResetAll()
        {
            base.ResetAll();
            SessionRevenue = 0;
        }
    }
}
