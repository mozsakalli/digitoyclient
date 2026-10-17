// core.upm Network/ClientAuthParams.cs — birebir.
namespace Digitoy
{
    /// <summary>Yalnız analitiğe giden, client'ın sahibi olduğu login Params anahtarları. Sunucu bunları okumaz;
    /// <c>AuthRequest.Params</c> ile olduğu gibi <c>report_connect.player_meta</c>'ya geçer (anahtar ≤32, değer ≤256 kr, toplam ≤32 anahtar).
    /// Karar anahtarları (deviceKey, os, platform…) Common'daki <c>M.AuthRequestParams</c>'ta kalır.
    /// Wire değerleri eski AuthRequestParams ile birebir aynı (ClickHouse attribution sürekliliği).</summary>
    internal static class ClientAuthParams
    {
        public const string Model = "model";
        public const string Connection = "connection";
        public const string Carrier = "carrier";
        public const string EmulatorScore = "emulatorScore";
        /// <summary>Seçilen attribution kaynağı (Adjust/AppsFlyer; bkz. Server.SelectAttributionSource).</summary>
        public const string Source = "source";
        public const string AppsflyerId = "appsflyerId";
        /// <summary>Attribution artık yalnız login Params'ından gider (UpdateMeta ucu 2026-10-08'de kaldırıldı).</summary>
        public const string AdjustAdid = "adjustAdid";
        public const string FirebaseInstanceId = "firebaseInstanceId";
    }
}
