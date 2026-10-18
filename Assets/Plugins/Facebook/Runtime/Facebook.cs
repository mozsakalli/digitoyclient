using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;

// Facebook Login plugin'i (game_rd docs/plugins.md). core.upm Runtime/Components/Auth/Facebook.cs portu:
// - tek seferde tek login (_loginInProgress), tekrar cagri Fail doner;
// - SDK'da gecerli (suresi dolmamis) token varsa login ekrani acilmaz ("status" istegi);
// - iOS ATT/Limited login ve timeScale dondurma Android fazinda YOK (iOS fazinda ele alinir).
// Protokol (plugin'in kendi secimi; core bilmez): JSON, "id" ile korelasyon.
//   -> {"id":1,"method":"status"}                                    <- {"id":1,"ok":true,"token":"..","userId":"..","expires":unixSec} | {"id":1,"ok":false}
//   -> {"id":2,"method":"login","permissions":["public_profile","email"]}
//   <- {"id":2,"ok":true,"token":"...","userId":"..."} | {"id":2,"ok":false,"error":"cancel"|"<mesaj>"}
//   -> {"method":"logout"}
//   <- {"event":"deepLink","url":"..."}  (istenmemis; soguk baslangicta inbox'ta bekler)
// Runtime/ icinde #if ve DllImport YOK: editor/Windows/wasm'da ayni kod derlenir; host yoksa Available=false.
// Sonuc tipi motor-bagimsiz LoginResult; Digitoy Result<T>/ErrorCode eslemesi cagiranin (Server.LoginAsync) isi.
public sealed class Facebook : Plugin<Facebook.Config>
{
    [Serializable]
    public sealed class Config
    {
        [Required] public string appId = "";
        [Required, Sensitive] public string clientToken = "";
        public string displayName = "";
        public bool autoLogAppEvents = true;
    }

    public enum LoginStatus { Success, Cancel, Fail, Unavailable, Timeout, Busy }
    public struct LoginResult { public LoginStatus Status; public string Token, UserId, Error; }

    static readonly string[] DefaultPermissions = { "public_profile", "email" };

    readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
    int _nextId;
    bool _loginInProgress;

    public string LastToken { get; private set; }
    public string LastUserId { get; private set; }
    public string PendingDeepLink { get; private set; }

    public async Task<LoginResult> Login(float timeout = 60f, params string[] permissions)
    {
        if (!Available) return new LoginResult { Status = LoginStatus.Unavailable, Error = "no host plugin" };
        if (_loginInProgress) return new LoginResult { Status = LoginStatus.Busy, Error = "login in progress" };
        _loginInProgress = true;
        try
        {
            // Suresi dolmamis cached token: SDK ekrani acma (core.upm TryRefetchSessionData).
            var st = await Request("status", null, 5f);
            if (st != null && st.GetBool("ok") && !string.IsNullOrEmpty(st.GetString("token")))
            {
                long exp = st.GetLong("expires");
                if (exp <= 0 || exp > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
                    return Remember(st);
            }

            var perms = permissions != null && permissions.Length > 0 ? permissions : DefaultPermissions;
            var r = await Request("login", "\"permissions\":" + JsonArray(perms), timeout);
            if (r == null) return new LoginResult { Status = LoginStatus.Timeout, Error = "timeout" };
            if (r.GetBool("ok")) return Remember(r);
            string err = r.GetString("error", "fail");
            return new LoginResult { Status = err == "cancel" ? LoginStatus.Cancel : LoginStatus.Fail, Error = err };
        }
        finally { _loginInProgress = false; }
    }

    public void Logout()
    {
        LastToken = null;
        LastUserId = null;
        Send("{\"method\":\"logout\"}");
    }

    LoginResult Remember(JsonNode r)
    {
        LastToken = r.GetString("token");
        LastUserId = r.GetString("userId");
        return new LoginResult { Status = LoginStatus.Success, Token = LastToken, UserId = LastUserId };
    }

    // Tek-yanitli istek: id -> TCS; timeout/iptal -> null (exception yok; SDK hatasi sonuc degeridir).
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

    protected override void OnMessage(string msg)
    {
        var j = Json.Parse(msg);
        var idNode = j["id"];
        if (idNode != null)
        {
            if (_pending.Remove(idNode.AsInt(), out var tcs)) tcs.TrySetResult(j);
            return;
        }
        if (j.GetString("event") == "deepLink") PendingDeepLink = j.GetString("url");
    }

    static string JsonArray(string[] items)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("[");
        for (int i = 0; i < items.Length; i++) { if (i > 0) sb.Append(","); sb.Append("\"").Append(items[i]).Append("\""); }
        return sb.Append("]").ToString();
    }
}
