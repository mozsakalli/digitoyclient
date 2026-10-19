// Oyunun TEK ayar + giris nesnesi (game_rd docs/plugins.md "IGameBoot"): PlayerSettings.boot bunu gosterir; GameHost ve editor Play
// sahneden ONCE Boot() cagirir — Lobby'den Play'de de servisler hazir. Alanlar GOMULU (ayri GameDefinition/Settings dosyasi yok):
// Assets/Games/<Oyun>/Boot.asset icinde o oyunun kimligi, sahneleri ve sunucu ayari birlikte. Sahne/GO ile ilgisi yok.
using System;
using DigitoyEngine;

namespace Digitoy
{
    [Serializable]
    [Asset]
    public sealed class ClientBoot : IGameBoot
    {
        public GameDefinition game = new();
        public NetworkSettings network = new();

        public void Boot() => AppBoot.Ensure(game, network);
    }
}
