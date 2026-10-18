using DigitoyEngine;

// Editor Play'de Facebook host taklidi (game_rd docs/plugins.md): status -> oturum yok, login -> bir sonraki frame basari
// (CancelNext ile iptal). Oyun akisi editorde test edilir; gercek SDK yok.
public sealed class FacebookMock : PluginMock<Facebook>
{
    public bool CancelNext;
    bool _loggedIn;

    public override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        int id = j.GetInt("id");
        switch (j.GetString("method"))
        {
            case "status":
                Send(_loggedIn ? "{\"id\":" + id + ",\"ok\":true,\"token\":\"mock-token\",\"userId\":\"42\",\"expires\":0}"
                               : "{\"id\":" + id + ",\"ok\":false}");
                break;
            case "login":
                if (CancelNext) { CancelNext = false; Send("{\"id\":" + id + ",\"ok\":false,\"error\":\"cancel\"}"); }
                else { _loggedIn = true; Send("{\"id\":" + id + ",\"ok\":true,\"token\":\"mock-token\",\"userId\":\"42\"}"); }
                break;
            case "logout":
                _loggedIn = false;
                break;
        }
    }
}
