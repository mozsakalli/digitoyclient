package com.digitoy.plugins.facebook

import android.content.Intent
import android.os.Bundle
import com.digitoy.host.DEPlugin
import com.facebook.AccessToken
import com.facebook.CallbackManager
import com.facebook.FacebookCallback
import com.facebook.FacebookException
import com.facebook.login.LoginManager
import com.facebook.login.LoginResult
import org.json.JSONArray
import org.json.JSONObject

// Facebook host plugin'i (game_rd docs/plugins.md). Ad = managed sinifin kisa adi ("Facebook"). onMessage main thread'de,
// base try/catch. Yanit: ayni "id" ile send(). SDK callback'leri (FacebookCallback) main thread'de gelir; govde kisa (docs "Garanti").
// SDK init: manifest meta-data (ApplicationId/ClientToken) ile otomatik (FacebookInitProvider); FB.Init esdegeri yok.
class FacebookPlugin : DEPlugin("Facebook") {
    private val callbacks = CallbackManager.Factory.create()
    private var pendingId = -1

    override fun onCreate(saved: Bundle?) {
        LoginManager.getInstance().registerCallback(callbacks, object : FacebookCallback<LoginResult> {
            override fun onSuccess(result: LoginResult) = reply(true, result.accessToken.token, result.accessToken.userId, null)
            override fun onCancel() = reply(false, null, null, "cancel")
            override fun onError(error: FacebookException) = reply(false, null, null, error.message ?: "error")
        })
        activity.intent?.data?.let { send(JSONObject().put("event", "deepLink").put("url", it.toString()).toString()) }
    }

    override fun onMessage(msg: String) {
        val j = JSONObject(msg)
        when (j.optString("method")) {
            "status" -> {
                val id = j.optInt("id", -1)
                val t = AccessToken.getCurrentAccessToken()
                val r = JSONObject().put("id", id)
                if (t != null && !t.isExpired) {
                    r.put("ok", true).put("token", t.token).put("userId", t.userId).put("expires", t.expires.time / 1000)
                } else r.put("ok", false)
                send(r.toString())
            }
            "login" -> {
                if (pendingId >= 0) { send(JSONObject().put("id", j.optInt("id", -1)).put("ok", false).put("error", "busy").toString()); return }
                pendingId = j.optInt("id", -1)
                val perms = ArrayList<String>()
                val arr: JSONArray? = j.optJSONArray("permissions")
                if (arr != null) for (i in 0 until arr.length()) perms.add(arr.getString(i))
                LoginManager.getInstance().logInWithReadPermissions(activity, callbacks, perms)
            }
            "logout" -> LoginManager.getInstance().logOut()
        }
    }

    private fun reply(ok: Boolean, token: String?, userId: String?, error: String?) {
        if (pendingId < 0) return
        val r = JSONObject().put("id", pendingId).put("ok", ok)
        if (token != null) r.put("token", token)
        if (userId != null) r.put("userId", userId)
        if (error != null) r.put("error", error)
        pendingId = -1
        send(r.toString())
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        callbacks.onActivityResult(requestCode, resultCode, data)
    }

    override fun onNewIntent(intent: Intent) {
        intent.data?.let { send(JSONObject().put("event", "deepLink").put("url", it.toString()).toString()) }
    }
}
