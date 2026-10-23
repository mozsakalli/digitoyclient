// Uygulama kurulumu (bir kez): aktif oyun, World kablosu (World.Initialize), reklam. Servis katmanı yok: bağlantı/modeller/istekler
// World'de (statik), tercihler Prefs.
using DigitoyEngine;

namespace Digitoy
{
    public static class AppBoot
    {
        public static bool IsReady { get; private set; }

        public static void Ensure(GameDefinition game, NetworkSettings network)
        {
            if (IsReady) return;
            if (game == null || network == null)
            {
                Debug.LogError("AppBoot: game/network bağlı değil");
                return;
            }
            // Surum TEK kaynaktan: PlayerSettings.version (release pak .project, editor Play) -> GameHost.Version. Kopya tutulmaz.
            Platform.AppVersion = string.IsNullOrEmpty(GameHost.Version) ? "0.0.0" : GameHost.Version;
            Game.Set(game);
            if (World.IsInitialized) World.Disconnect(M.CloseReason.Logout);
            World.Initialize(network);
            Ads.Init().Forget(); // reklam SDK'si arka planda; host yoksa sessizce false
            IsReady = true;
        }
    }
}
