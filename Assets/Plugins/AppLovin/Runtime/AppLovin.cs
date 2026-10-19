using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;

// AppLovin MAX plugin'i (game_rd docs/plugins.md "AppLovin"). Faz 1: interstitial + rewarded; banner/MREC sonraki faz.
// Runtime/ icinde #if ve DllImport YOK: editor/Windows/wasm'da ayni kod derlenir; host yoksa Available=false (gosterim
// "Unavailable" doner, oyun olmez). Ayarlar oyun basina (ProjectSettings/Games/<ad>/Plugins/AppLovin.asset): her oyunun
// kendi SDK key'i/ad unit'leri. Platform secimi (android/ios unit haritasi) HOST'ta: init mesaji iki haritayi da tasir,
// host kendininkini alir -> managed platform bilmek zorunda degil.
// Protokol (plugin'in kendi secimi): JSON, "id" ile korelasyon (istek/yanit) + "event" (istenmemis bildirimler).
//   -> {"id":1,"method":"init","sdkKey":"..","verbose":false,"testDevices":[..],"units":{"android":{"interstitial":"..","rewarded":".."},"ios":{..}}}
//   <- {"id":1,"ok":true,"consentFlowUserGeography":"gdpr|other|unknown"} | {"id":1,"ok":false,"error":".."}
//   -> {"method":"load","unit":"interstitial"|"rewarded"}     (yanit yok; sonuc event)
//   -> {"method":"show","unit":"rewarded","placement":"lobby_bonus"}
//   -> {"method":"privacy","hasUserConsent":true,"doNotSell":false,"ageRestricted":false}
//   -> {"method":"debugger"}
//   <- {"event":"loaded","unit":"rewarded","network":"Google AdMob"}
//   <- {"event":"loadFailed","unit":"rewarded","error":"no fill","code":204}
//   <- {"event":"displayed","unit":"..."} | {"event":"displayFailed","unit":"..","error":".."} | {"event":"clicked","unit":".."}
//   <- {"event":"rewarded","unit":"rewarded","amount":1,"label":"coins"}
//   <- {"event":"hidden","unit":"..."}
//   <- {"event":"revenue","unit":"..","revenue":0.0012,"network":"..","placement":"..","adUnitId":".."}   (ILRD)
// Oto-retry (MAX onerisi: 2^n saniye, en fazla 64 s) MANAGED tarafta — host ince kalir, davranis her platformda ayni.
public sealed class AppLovin : Plugin<AppLovin.Config>
{
    [Serializable]
    public sealed class AdUnits
    {
        public string interstitial = "";
        public string rewarded = "";
    }

    // Mediation adapter satiri: Editor hook'u Gradle/SPM bagimliligini buradan yazar. Liste elle yazilmaz: Player Settings'te
    // katalogdan secilir (Editor/AppLovinMediationDrawer.cs). network = AppLovin Maven artefakt adi (google, facebook, ironsource,
    // mintegral, pangle, unityads, vungle, inmobi, ...); displayName gorunen ad.
    [Serializable]
    public sealed class Adapter
    {
        public string network = "";
        public string displayName = "";
        public string android = "";
        public string ios = "";
    }

    [Serializable]
    public sealed class Config
    {
        [Required, Sensitive] public string sdkKey = "";
        public AdUnits android = new();
        public AdUnits ios = new();
        // AdMob (google adapter) uygulama kimlikleri: Android manifest meta-data / iOS GADApplicationIdentifier.
        public string admobAppIdAndroid = "";
        public string admobAppIdIos = "";
        public List<string> testDeviceIds = new();
        public bool verboseLogging;
        public string sdkVersionAndroid = "13.6.4";
        public string sdkVersionIos = "13.6.4";
        public List<Adapter> mediation = new() { new Adapter { network = "google", displayName = "Google AdMob", android = "25.5.0.0", ios = "13.11.0.0" } };
    }

    public enum ShowStatus { Shown, Rewarded, NotReady, Failed, Unavailable, Timeout, Busy }
    public struct ShowResult { public ShowStatus Status; public string Error; public int RewardAmount; public string RewardLabel; }

    public sealed class RevenueEvent { public string Unit, Network, Placement, AdUnitId; public double Revenue; }

    // Tam ekran reklam birimi (interstitial / rewarded): yukle-goster dongusu + oto-retry.
    public sealed class FullscreenAd
    {
        readonly AppLovin _owner;
        public readonly string Unit; // "interstitial" | "rewarded"
        public bool IsReady { get; private set; }
        public bool IsLoading { get; private set; }
        public string LastError { get; private set; }
        public string Network { get; private set; }
        public bool AutoRetry = true;
        int _retry;
        TaskCompletionSource<ShowResult> _showing;
        bool _rewarded;
        int _rewardAmount;
        string _rewardLabel;

        internal FullscreenAd(AppLovin owner, string unit) { _owner = owner; Unit = unit; }

        public void Load()
        {
            if (!_owner.Available || IsLoading || IsReady) return;
            IsLoading = true;
            _owner.Send("{\"method\":\"load\",\"unit\":\"" + Unit + "\"}");
        }

        // Gosterim: hidden'a kadar bekler (rewarded ise odul geldiyse Rewarded). Hazir degilse NotReady + Load tetikler.
        public async Task<ShowResult> Show(string placement = null, float timeout = 120f)
        {
            if (!_owner.Available) return new ShowResult { Status = ShowStatus.Unavailable, Error = "no host plugin" };
            if (_showing != null) return new ShowResult { Status = ShowStatus.Busy, Error = "show in progress" };
            if (!IsReady) { Load(); return new ShowResult { Status = ShowStatus.NotReady, Error = LastError }; }
            _showing = new TaskCompletionSource<ShowResult>();
            _rewarded = false; _rewardAmount = 0; _rewardLabel = null;
            using var cts = Wait.Timeout(timeout);
            using var reg = cts.Token.Register(() => _showing?.TrySetResult(new ShowResult { Status = ShowStatus.Timeout, Error = "timeout" }));
            if (!_owner.Send("{\"method\":\"show\",\"unit\":\"" + Unit + "\"" + (placement != null ? ",\"placement\":\"" + Esc(placement) + "\"" : "") + "}"))
            {
                _showing = null;
                return new ShowResult { Status = ShowStatus.Failed, Error = "send failed" };
            }
            var r = await _showing.Task;
            _showing = null;
            return r;
        }

        internal void OnEvent(string ev, JsonNode j)
        {
            switch (ev)
            {
                case "loaded":
                    IsLoading = false; IsReady = true; _retry = 0; LastError = null; Network = j.GetString("network");
                    _owner.OnLoaded?.Invoke(this);
                    break;
                case "loadFailed":
                    IsLoading = false; IsReady = false; LastError = j.GetString("error", "load failed");
                    _owner.OnLoadFailed?.Invoke(this, LastError);
                    if (AutoRetry) _ = _owner.ScheduleRetry(this, ++_retry);
                    break;
                case "displayed":
                    IsReady = false; // MAX: gosterilen reklam tuketildi, hidden sonrasi yeniden yuklenir
                    break;
                case "displayFailed":
                    IsReady = false;
                    _showing?.TrySetResult(new ShowResult { Status = ShowStatus.Failed, Error = j.GetString("error", "display failed") });
                    Load(); // yeni istek
                    break;
                case "rewarded":
                    _rewarded = true; _rewardAmount = j.GetInt("amount", 1); _rewardLabel = j.GetString("label");
                    break;
                case "hidden":
                    _showing?.TrySetResult(new ShowResult
                    {
                        Status = _rewarded ? ShowStatus.Rewarded : ShowStatus.Shown,
                        RewardAmount = _rewardAmount, RewardLabel = _rewardLabel,
                    });
                    Load(); // MAX onerisi: hidden'da hemen bir sonrakini yukle
                    break;
            }
        }
    }

    public readonly FullscreenAd Interstitial;
    public readonly FullscreenAd Rewarded;

    public bool Initialized { get; private set; }
    public string ConsentFlowUserGeography { get; private set; } = "unknown";
    public event Action<FullscreenAd> OnLoaded;
    public event Action<FullscreenAd, string> OnLoadFailed;
    public event Action<RevenueEvent> OnRevenue;

    readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
    int _nextId;
    Task<bool> _initTask;

    public AppLovin()
    {
        Interstitial = new FullscreenAd(this, "interstitial");
        Rewarded = new FullscreenAd(this, "rewarded");
    }

    // SDK init (bir kez; tekrar cagri ayni Task; basarisizsa yeniden denenebilir). Basarida unit'ler yuklenmeye baslar.
    public Task<bool> Init(float timeout = 30f)
    {
        if (_initTask != null) return _initTask;
        _initTask = InitCore(timeout);
        return _initTask;
    }

    async Task<bool> InitCore(float timeout)
    {
        if (!Available) return false;
        var s = Settings;
        var sb = new System.Text.StringBuilder();
        sb.Append("\"sdkKey\":\"").Append(Esc(s.sdkKey)).Append("\",\"verbose\":").Append(s.verboseLogging ? "true" : "false");
        sb.Append(",\"testDevices\":").Append(JsonArray(s.testDeviceIds));
        sb.Append(",\"units\":{\"android\":").Append(Units(s.android)).Append(",\"ios\":").Append(Units(s.ios)).Append("}");
        var r = await Request("init", sb.ToString(), timeout);
        if (r == null || !r.GetBool("ok"))
        {
            Debug.LogWarning("[applovin] init basarisiz: " + (r?.GetString("error") ?? "timeout"));
            _initTask = null; // tekrar denenebilsin
            return false;
        }
        Initialized = true;
        ConsentFlowUserGeography = r.GetString("consentFlowUserGeography", "unknown");
        Interstitial.Load();
        Rewarded.Load();
        return true;
    }

    // GDPR/CCPA/COPPA bayraklari (CMP sonucu ya da oyunun kendi akisi); init'ten once de cagrilabilir.
    public void SetPrivacy(bool hasUserConsent, bool doNotSell = false, bool ageRestricted = false)
        => Send("{\"method\":\"privacy\",\"hasUserConsent\":" + (hasUserConsent ? "true" : "false")
            + ",\"doNotSell\":" + (doNotSell ? "true" : "false") + ",\"ageRestricted\":" + (ageRestricted ? "true" : "false") + "}");

    public void ShowMediationDebugger() => Send("{\"method\":\"debugger\"}");

    protected override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        var idNode = j["id"];
        if (idNode != null)
        {
            if (_pending.Remove(idNode.AsInt(), out var tcs)) tcs.TrySetResult(j);
            return;
        }
        string ev = j.GetString("event", "");
        if (ev == "revenue")
        {
            OnRevenue?.Invoke(new RevenueEvent
            {
                Unit = j.GetString("unit"), Network = j.GetString("network"), Placement = j.GetString("placement"),
                AdUnitId = j.GetString("adUnitId"), Revenue = j.GetDouble("revenue"),
            });
            return;
        }
        string unit = j.GetString("unit", "");
        var ad = unit == "rewarded" ? Rewarded : unit == "interstitial" ? Interstitial : null;
        ad?.OnEvent(ev, j);
    }

    // Ustel geri cekilme: 2,4,8,16,32,64 s (MAX dokumani).
    async Task ScheduleRetry(FullscreenAd ad, int attempt)
    {
        int delay = 1 << Math.Min(6, Math.Max(1, attempt));
        await Wait.Seconds(delay);
        if (Available && !ad.IsReady) ad.Load();
    }

    async Task<JsonNode> Request(string method, string extraJson, float timeout)
    {
        int id = ++_nextId;
        var tcs = new TaskCompletionSource<JsonNode>();
        _pending[id] = tcs;
        using var cts = Wait.Timeout(timeout);
        using var reg = cts.Token.Register(() => { _pending.Remove(id); tcs.TrySetResult(null); });
        if (!Send("{\"id\":" + id + ",\"method\":\"" + method + "\"" + (extraJson != null ? "," + extraJson : "") + "}"))
        {
            _pending.Remove(id);
            return null;
        }
        return await tcs.Task;
    }

    static string Units(AdUnits u)
        => "{\"interstitial\":\"" + Esc(u.interstitial) + "\",\"rewarded\":\"" + Esc(u.rewarded) + "\"}";

    static string JsonArray(List<string> items)
    {
        var sb = new System.Text.StringBuilder("[");
        for (int i = 0; i < items.Count; i++) { if (i > 0) sb.Append(","); sb.Append("\"").Append(Esc(items[i])).Append("\""); }
        return sb.Append("]").ToString();
    }

    static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
