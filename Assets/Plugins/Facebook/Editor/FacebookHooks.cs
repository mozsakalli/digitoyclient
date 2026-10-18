using DigitoyEngine.Build;

// Facebook plugin build kancalari (docs/plugins.md). Editor/ -> <Ad>.Editor.dll: AOT'ye ve oyun assembly'sine GIRMEZ.
// Kanca sinif adi plugin adiyla baslar ("Facebook...") -> plugin canli degilse calismaz.
// Android bagimliliklari (facebook-login) ve manifest meta-data'si Android/ Gradle LIBRARY modulunde; burada yalniz
// app-seviyesi isler: manifest'in @string ile okudugu degerler (library manifest'i placeholder goremez, resString gorur).
public sealed class FacebookAndroid : IAndroidProjectHook
{
    public void OnGenerate(AndroidProject p)
    {
        var s = p.Settings<Facebook.Config>();
        p.ResString("facebook_app_id", s.appId);
        p.ResString("facebook_client_token", s.clientToken);
        p.ResString("fb_login_protocol_scheme", "fb" + s.appId);
    }
}

public sealed class FacebookIos : IIosProjectHook
{
    public void OnGenerate(IosProject p)
    {
        var s = p.Settings<Facebook.Config>();
        // Yalniz Login urunu (FBSDKLoginKit + CoreKit); Share/Gaming girmez (iOS'ta trim = product secimi).
        p.SwiftPackage("https://github.com/facebook/facebook-ios-sdk", "17.0.0", "FacebookLogin");
        p.Plist["FacebookAppID"] = s.appId;
        p.Plist["FacebookClientToken"] = s.clientToken;
        p.Plist["FacebookDisplayName"] = string.IsNullOrEmpty(s.displayName) ? "Game" : s.displayName;
        p.Plist["FacebookAutoLogAppEventsEnabled"] = s.autoLogAppEvents;
        p.Plist["LSApplicationQueriesSchemes"] = new[] { "fbapi", "fb-messenger-share-api" };
        p.UrlScheme("fb" + s.appId);
    }
}

public sealed class FacebookWasm : IWasmProjectHook
{
    public void OnGenerate(WasmProject p)
    {
        var s = p.Settings<Facebook.Config>();
        p.Script("https://connect.facebook.net/en_US/sdk.js");
        p.ConfigValue("facebookAppId", s.appId);
    }
}
