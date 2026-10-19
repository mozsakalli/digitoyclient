package com.digitoy.plugins.applovin

import android.os.Bundle
import com.applovin.mediation.MaxAd
import com.applovin.mediation.MaxAdListener
import com.applovin.mediation.MaxAdRevenueListener
import com.applovin.mediation.MaxError
import com.applovin.mediation.MaxReward
import com.applovin.mediation.MaxRewardedAdListener
import com.applovin.mediation.ads.MaxInterstitialAd
import com.applovin.mediation.ads.MaxRewardedAd
import com.applovin.sdk.AppLovinMediationProvider
import com.applovin.sdk.AppLovinPrivacySettings
import com.applovin.sdk.AppLovinSdk
import com.applovin.sdk.AppLovinSdkConfiguration
import com.applovin.sdk.AppLovinSdkInitializationConfiguration
import com.digitoy.host.DEPlugin
import org.json.JSONArray
import org.json.JSONObject

// AppLovin MAX host plugin'i (game_rd docs/plugins.md). Ad = managed sinifin kisa adi ("AppLovin"). onMessage main thread'de,
// base try/catch. Protokol Runtime/AppLovin.cs basliginda. Host INCE: retry/durum managed'da; burada yalniz SDK cagrilari ve
// listener -> send(event). Unit haritasi init mesajindaki "units.android"dan alinir (platform secimi burada).
class AppLovinPlugin : DEPlugin("AppLovin") {
    private var interstitial: MaxInterstitialAd? = null
    private var rewarded: MaxRewardedAd? = null
    private var units = JSONObject()

    override fun onMessage(msg: String) {
        val j = JSONObject(msg)
        when (j.optString("method")) {
            "init" -> init(j)
            "load" -> when (j.optString("unit")) {
                "interstitial" -> if (interstitial != null) interstitial!!.loadAd() else send(event("loadFailed", "interstitial").put("error", "not initialized").toString())
                "rewarded" -> if (rewarded != null) rewarded!!.loadAd() else send(event("loadFailed", "rewarded").put("error", "not initialized").toString())
            }
            "show" -> {
                val placement: String? = if (j.has("placement")) j.optString("placement") else null
                when (j.optString("unit")) {
                    "interstitial" -> interstitial?.let { if (it.isReady) { if (placement != null) it.showAd(placement) else it.showAd() } else fail("interstitial", "not ready") }
                    "rewarded" -> rewarded?.let { if (it.isReady) { if (placement != null) it.showAd(placement) else it.showAd() } else fail("rewarded", "not ready") }
                }
            }
            "privacy" -> {
                val ctx = application
                AppLovinPrivacySettings.setHasUserConsent(j.optBoolean("hasUserConsent", false), ctx)
                AppLovinPrivacySettings.setDoNotSell(j.optBoolean("doNotSell", false), ctx)
                // ageRestricted: SDK 13'te kaldirildi (yas sinirlamasi artik dashboard/CMP tarafinda); mesajdaki bayrak yok sayilir.
            }
            "debugger" -> AppLovinSdk.getInstance(application).showMediationDebugger()
        }
    }

    private fun init(j: JSONObject) {
        val id = j.optInt("id", -1)
        val sdkKey = j.optString("sdkKey", "")
        if (sdkKey.isEmpty()) { send(JSONObject().put("id", id).put("ok", false).put("error", "sdkKey empty").toString()); return }
        units = j.optJSONObject("units")?.optJSONObject("android") ?: JSONObject()
        val ctx = application
        val builder = AppLovinSdkInitializationConfiguration.builder(sdkKey, ctx).setMediationProvider(AppLovinMediationProvider.MAX)
        val tests = j.optJSONArray("testDevices")
        if (tests != null && tests.length() > 0) {
            val list = ArrayList<String>()
            for (i in 0 until tests.length()) list.add(tests.getString(i))
            builder.setTestDeviceAdvertisingIds(list)
        }
        val sdk = AppLovinSdk.getInstance(ctx)
        sdk.settings.setVerboseLogging(j.optBoolean("verbose", false))
        sdk.initialize(builder.build()) { cfg: AppLovinSdkConfiguration ->
            createAds()
            val geo = when (cfg.consentFlowUserGeography) {
                AppLovinSdkConfiguration.ConsentFlowUserGeography.GDPR -> "gdpr"
                AppLovinSdkConfiguration.ConsentFlowUserGeography.OTHER -> "other"
                else -> "unknown"
            }
            send(JSONObject().put("id", id).put("ok", true).put("consentFlowUserGeography", geo).toString())
        }
    }

    private fun createAds() {
        val act = activityOrNull ?: return
        val interId = units.optString("interstitial", "")
        val rewId = units.optString("rewarded", "")
        if (interId.isNotEmpty()) interstitial = MaxInterstitialAd(interId, act).also {
            it.setListener(listener("interstitial"))
            it.setRevenueListener(revenue("interstitial"))
        }
        if (rewId.isNotEmpty()) rewarded = MaxRewardedAd.getInstance(rewId, act).also {
            it.setListener(rewardedListener())
            it.setRevenueListener(revenue("rewarded"))
        }
    }

    private fun event(name: String, unit: String) = JSONObject().put("event", name).put("unit", unit)
    private fun fail(unit: String, error: String) = send(event("displayFailed", unit).put("error", error).toString())

    private fun listener(unit: String) = object : MaxAdListener {
        override fun onAdLoaded(ad: MaxAd) = send(event("loaded", unit).put("network", ad.networkName).toString())
        override fun onAdLoadFailed(adUnitId: String, error: MaxError) =
            send(event("loadFailed", unit).put("error", error.message).put("code", error.code).toString())
        override fun onAdDisplayed(ad: MaxAd) = send(event("displayed", unit).toString())
        override fun onAdDisplayFailed(ad: MaxAd, error: MaxError) =
            send(event("displayFailed", unit).put("error", error.message).put("code", error.code).toString())
        override fun onAdClicked(ad: MaxAd) = send(event("clicked", unit).toString())
        override fun onAdHidden(ad: MaxAd) = send(event("hidden", unit).toString())
    }

    private fun rewardedListener() = object : MaxRewardedAdListener {
        private val base = listener("rewarded")
        override fun onAdLoaded(ad: MaxAd) = base.onAdLoaded(ad)
        override fun onAdLoadFailed(adUnitId: String, error: MaxError) = base.onAdLoadFailed(adUnitId, error)
        override fun onAdDisplayed(ad: MaxAd) = base.onAdDisplayed(ad)
        override fun onAdDisplayFailed(ad: MaxAd, error: MaxError) = base.onAdDisplayFailed(ad, error)
        override fun onAdClicked(ad: MaxAd) = base.onAdClicked(ad)
        override fun onAdHidden(ad: MaxAd) = base.onAdHidden(ad)
        override fun onUserRewarded(ad: MaxAd, reward: MaxReward) =
            send(event("rewarded", "rewarded").put("amount", reward.amount).put("label", reward.label).toString())
    }

    private fun revenue(unit: String) = MaxAdRevenueListener { ad ->
        send(event("revenue", unit).put("revenue", ad.revenue).put("network", ad.networkName)
            .put("placement", ad.placement ?: "").put("adUnitId", ad.adUnitId).toString())
    }

    override fun onCreate(saved: Bundle?) {
        // Activity sonradan geldiyse (init onCreate'ten once kostu) ad nesneleri simdi kurulur.
        if (interstitial == null && rewarded == null && units.length() > 0) createAds()
    }

    override fun onDestroy() {
        interstitial?.destroy(); interstitial = null
        rewarded?.destroy(); rewarded = null
    }
}
