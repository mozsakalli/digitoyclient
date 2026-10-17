// Unity GameProperties'in oyun-başına parçası (01-architecture §3). Hepsi build'de; runtime'da Game.Current seçer.
// Sahne alanları Faz 0'da yol string'i; motor sahne referans tipi netleşince değişir.
using System;
using DigitoyEngine;

namespace Digitoy
{
    [Serializable]
    [CreateAssetMenu(MenuName = "Digitoy/Game Definition", FileName = "Game")]
    public class GameDefinition
    {
        public string id = "ginrummy";
        public string displayName = "Gin Rummy";
        public int gameId = 7;
        public int maxSeats = 2;

        public string loginScene = "";
        public string lobbyScene = "";
        public string tableScene = "";
    }

    /// <summary>Aktif oyun. Başlangıçta AppVariant.startGame; Faz 3'te Game.Switch (logout + relogin) eklenir.</summary>
    public static class Game
    {
        public static GameDefinition Current { get; private set; }

        public static void Set(GameDefinition def) => Current = def;
    }
}
