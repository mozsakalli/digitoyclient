using DigitoyEngine.Build;

// AppLovin MAX plugin build kancalari (docs/plugins.md). Editor/ -> <Ad>.Editor.dll: AOT'ye ve oyun assembly'sine GIRMEZ.
// Kanca sinif adi plugin adiyla baslar ("AppLovin...") -> plugin canli degilse calismaz.
// BAGIMLILIKLAR BURADA (Android/ library modulunde degil): SDK surumu ve adapter listesi oyunun Config'inden gelir
// (ProjectSettings/Games/<ad>/Plugins/AppLovin.asset) -> app seviyesine yazilir. Surum guncelleme = Config'i duzenle.
public sealed class AppLovinAndroid : IAndroidProjectHook
{
    public void OnGenerate(AndroidProject p)
    {
        var s = p.Settings<AppLovin.Config>();
        p.ResString("admob_app_id", s.admobAppIdAndroid); // library manifest'i @string ile okur
        p.Dependency("com.applovin:applovin-sdk:" + s.sdkVersionAndroid);
        foreach (var a in s.mediation)
        {
            if (string.IsNullOrWhiteSpace(a.network) || string.IsNullOrWhiteSpace(a.android)) continue;
            string n = a.network.Trim().ToLowerInvariant();
            p.Dependency("com.applovin.mediation:" + n + "-adapter:" + a.android.Trim());
            // Maven Central disinda yasayan network'ler (AppLovin adapter dokumanlari).
            switch (n)
            {
                case "pangle": p.MavenRepo("https://artifact.bytedance.com/repository/pangle"); break;
                case "mintegral": p.MavenRepo("https://dl-maven-android.mintegral.com/repository/mbridge_android_sdk_oversea"); break;
                case "yandex": p.MavenRepo("https://maven.google.com"); break;
                case "ironsource": p.MavenRepo("https://android-sdk.is.com/"); break;
                case "chartboost": p.MavenRepo("https://cboost.jfrog.io/artifactory/chartboost-ads/"); break;
                case "tencent": p.MavenRepo("https://maven.aliyun.com/repository/public"); break;
            }
        }
        if (s.mediation.Exists(a => a.network.Trim().ToLowerInvariant() == "google") && string.IsNullOrWhiteSpace(s.admobAppIdAndroid))
            p.Warn("google adapter var ama admobAppIdAndroid bos: AdMob init'te uygulama coker (manifest APPLICATION_ID)");
    }
}

public sealed class AppLovinIos : IIosProjectHook
{
    public void OnGenerate(IosProject p)
    {
        var s = p.Settings<AppLovin.Config>();
        // Faz 2'de tamamlanir: core SDK SPM; adapter'larin SPM kapsami network'e gore (yoksa XcFramework / kabukta CocoaPods).
        p.SwiftPackage("https://github.com/AppLovin/AppLovin-MAX-Swift-Package", s.sdkVersionIos, "AppLovinSDK");
        if (!string.IsNullOrWhiteSpace(s.admobAppIdIos)) p.Plist["GADApplicationIdentifier"] = s.admobAppIdIos;
        p.Plist["NSUserTrackingUsageDescription"] = "This identifier will be used to deliver personalized ads to you.";
        // AppLovin SKAdNetwork kimligi (tam liste Faz 2'de AppLovin'in yayimladigi plist'ten uretilir).
        p.Plist["SKAdNetworkItems"] = new[] { "ludvb6z3bs.skadnetwork" };
        if (s.mediation.Count > 0) p.Warn("iOS adapter bagimliliklari Faz 2: " + s.mediation.Count + " adapter henuz yazilmiyor (yalniz AppLovinSDK)");
    }
}
