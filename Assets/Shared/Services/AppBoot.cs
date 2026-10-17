// Uygulama kurulumu (bir kez): servisler, aktif oyun, Server. Login sahnesi Start'ta çağırır; AppVariant (motor A1/A2)
// gelince game/network oradan okunur.
using DigitoyEngine;

namespace Digitoy
{
    public static class AppBoot
    {
        public static bool IsReady { get; private set; }

        public static void Ensure(GameDefinition game, NetworkSettings network, string appVersion)
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
                _ = new TableService();
                _ = new LobbyService();
            }
            Platform.AppVersion = appVersion;
            Game.Set(game);
            if (Server.IsInitialized) Server.Disconnect(M.CloseReason.Logout);
            Server.Initialize(network);
            IsReady = true;
        }
    }
}
