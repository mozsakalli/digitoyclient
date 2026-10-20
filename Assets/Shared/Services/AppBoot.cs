// Uygulama kurulumu (bir kez): servisler, aktif oyun, Server. Login sahnesi Start'ta çağırır; AppVariant (motor A1/A2)
// gelince game/network oradan okunur.
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
            if (PlayerPrefsService.I == null)
            {
                _ = new PlayerPrefsService();
                _ = new PlayerService();
                _ = TableServices.Create(game.gameId);   // oyun türevi (GinTableService) kayıtlıysa o; Game.Switch'te yeniden kurulacak (Faz 3)
                _ = new LobbyService();
                _ = new AdService();
            }
            // Surum TEK kaynaktan: PlayerSettings.version (release pak .project, editor Play) -> GameHost.Version. Kopya tutulmaz.
            Platform.AppVersion = string.IsNullOrEmpty(GameHost.Version) ? "0.0.0" : GameHost.Version;
            Game.Set(game);
            if (Server.IsInitialized) Server.Disconnect(M.CloseReason.Logout);
            Server.Initialize(network);
            AdService.I.Init().Forget(); // reklam SDK'si arka planda; host yoksa sessizce false
            IsReady = true;
        }
    }
}
