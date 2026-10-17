// core.upm Helpers/Service.cs adaptasyonu. Fark: Debug.LogException → Log.Error; RuntimeInitializeOnLoadMethod yok
// (ALC reload'da statikler zaten sıfırlanır); Faz 0'da olmayan statik önbellekler (Config, RemoteContent, EventPool,
// AvatarCatalog, GiftCatalog) ResetSessionStatics'ten çıkarıldı — geldikçe eklenir.
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace Digitoy
{
    /// <summary>
    /// Uzun ömürlü singleton servislerin tabanı (PlayerService, TableService, …). Örnek bir kez yaratılır, yeniden
    /// yaratılmaz; yerinde sıfırlanır: <see cref="ResetSession"/> oyuncu verisi (logout / oyun değişimi),
    /// <see cref="ResetAll"/> her şey.
    /// </summary>
    public abstract class Service
    {
        protected Service() => ServiceRegistry.Register(this);

        /// <summary>Oyuncuya özel durumu temizle (önbellek, mevcut masa, oturum event'leri).</summary>
        protected internal virtual void ResetSession() { }

        /// <summary>Tüm durumu temizle. Varsayılan: <see cref="ResetSession"/>.</summary>
        protected internal virtual void ResetAll() => ResetSession();
    }

    /// <summary>Tüm <see cref="Service"/> örneklerini ve event'lerle katılan statik önbellekleri izler.</summary>
    public static class ServiceRegistry
    {
        private static readonly List<Service> Services = new();

        /// <summary>Tüm servislerin <see cref="Service.ResetSession"/>'ından sonra.</summary>
        public static event Action OnResetSession;
        /// <summary>Tüm servislerin <see cref="Service.ResetAll"/>'ından sonra.</summary>
        public static event Action OnResetAll;

        public static IReadOnlyList<Service> All => Services;

        internal static void Register(Service service)
        {
            if (!Services.Contains(service)) Services.Add(service);
        }

        /// <summary>Logout / oyun değişimi: yalnız oyuncu durumu.</summary>
        public static void ResetSession()
        {
            foreach (var s in Services.ToArray())
            {
                try { s.ResetSession(); }
                catch (Exception e) { Debug.LogError(e); }
            }
            ResetSessionStatics();
            OnResetSession?.Invoke();
        }

        /// <summary>Tam sıfırlama.</summary>
        public static void ResetAll()
        {
            foreach (var s in Services.ToArray())
            {
                try { s.ResetAll(); }
                catch (Exception e) { Debug.LogError(e); }
            }
            ResetSessionStatics();
            OnResetAll?.Invoke();
        }

        // Oturumdan türeyen çekirdek statik önbellekler.
        private static void ResetSessionStatics()
        {
            TimeSync.Reset();
        }
    }
}
