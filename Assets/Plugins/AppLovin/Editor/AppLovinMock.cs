using DigitoyEngine;

// Editor Play'de AppLovin host taklidi (game_rd docs/plugins.md): init -> ok; load -> hemen "loaded" (inbox bir sonraki frame
// teslim eder); show -> displayed, (rewarded ise) rewarded, hidden. Senaryo anahtarlari: NoFill (load basarisiz), FailShow,
// SkipReward (rewarded'da odul gelmez). Oyun akisi editorde test edilir; gercek SDK yok.
public sealed class AppLovinMock : PluginMock<AppLovin>
{
    public bool NoFill, FailShow, SkipReward;

    public override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        string unit = j.GetString("unit", "");
        switch (j.GetString("method"))
        {
            case "init":
                Send("{\"id\":" + j.GetInt("id") + ",\"ok\":true,\"consentFlowUserGeography\":\"other\"}");
                break;
            case "load":
                if (NoFill) Send("{\"event\":\"loadFailed\",\"unit\":\"" + unit + "\",\"error\":\"no fill (mock)\",\"code\":204}");
                else Send("{\"event\":\"loaded\",\"unit\":\"" + unit + "\",\"network\":\"Mock\"}");
                break;
            case "show":
                if (FailShow) { Send("{\"event\":\"displayFailed\",\"unit\":\"" + unit + "\",\"error\":\"display failed (mock)\"}"); break; }
                Send("{\"event\":\"displayed\",\"unit\":\"" + unit + "\"}");
                Send("{\"event\":\"revenue\",\"unit\":\"" + unit + "\",\"revenue\":0.001,\"network\":\"Mock\",\"placement\":\"" + j.GetString("placement", "") + "\",\"adUnitId\":\"mock\"}");
                if (unit == "rewarded" && !SkipReward) Send("{\"event\":\"rewarded\",\"unit\":\"rewarded\",\"amount\":1,\"label\":\"coins\"}");
                Send("{\"event\":\"hidden\",\"unit\":\"" + unit + "\"}");
                break;
            case "privacy":
            case "debugger":
                break;
        }
    }
}
