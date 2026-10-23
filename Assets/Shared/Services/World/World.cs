// World: sunucu kapısı — bağlantı (World.Network), oturum (World.Session), sunucunun client'taki aynası + olay üretici. İki giriş:
// Apply(msg) istek cevabı, Push(msg) sunucu push'u (oturum yüklenene kadar düşer). İkisi de aynı yoldan modele yazılır,
// önce/sonra farkından olaylar üretilir (WorldEvent.cs). Her Apply'da: model komple yazılır → olaylar ANINDA fırlar
// (kuyruk yok; paralel akışlar — kart uçarken rakip ayrılır — olduğu anda) → tur sonunda Changed(part) bir kez.
// Thread/bus yok; ana döngüde senkron. Dinleyici fırlatırsa yutulur (model bozulmaz).
// Tick(): saniyede bir (NetworkClient tick'inden) — zaman olayları (EventAvailable, InventoryReset).
using System;
using System.Collections.Generic;
using DigitoyEngine;
using M;

namespace Digitoy
{
    public static partial class World
    {
        /// <summary>Her olayı stdout'a yaz (taslak doğrulama; sonra kapatılır).</summary>
        public static bool Trace = true;

        /// <summary>Her olay (tür filtresiz).</summary>
        public static event Action<WorldEvent> OnEvent;
        /// <summary>Bir Apply/Tick turunda değişen parçalar; olaylardan SONRA, turda bir kez.</summary>
        public static event Action<WorldPart> Changed;

        static readonly Dictionary<Ev, Action<WorldEvent>> Handlers = new();
        static WorldPart _dirty;
        static int _depth;

        public static void On(Ev kind, Action<WorldEvent> h) { Handlers.TryGetValue(kind, out var cur); Handlers[kind] = cur + h; }
        public static void Off(Ev kind, Action<WorldEvent> h) { if (Handlers.TryGetValue(kind, out var cur)) Handlers[kind] = cur - h; }

        /// <summary>Sunucu push'u (requestId -1). Connect/reconnect sonrası oturum (PlayerState + masa tam görünümü) yüklenene kadar
        /// gelen push'lar DÜŞER: ayna henüz yok, delta uygulanamaz; tam görünüm Refresh cevabıyla gelir. İstek cevapları Apply'dan girer.</summary>
        public static void Push(object msg)
        {
            if (msg == null) return;
            if (!SessionReady) { if (Trace) Debug.Log($"[world] push düştü (oturum yükleniyor): {msg.GetType().Name}"); return; }
            Apply(msg);
        }

        /// <summary>Sunucudan gelen her gövde (istek cevabı ya da SessionReady sonrası push). Tanınmayan tip yok sayılır.</summary>
        public static void Apply(object msg)
        {
            if (msg == null) return;
            Begin();
            try
            {
                switch (msg)
                {
                    case TableState t: ApplyTable(t, reconcile: false); break;
                    case GameActionResult r: if (r.TableState != null) ApplyTable(r.TableState, reconcile: false); break;
                    case PlayerState p: ApplyPlayer(p); break;
                    case EventExecutionResult e: ApplyEventResult(e); break;
                    case FriendPreviewsResult f: ApplyFriends(f); break;
                    case PlayerPreview pv: ApplyPreview(pv); break;
                    case OnlineStatsResponse o: ApplyOnline(o); break;
                }
            }
            catch (Exception ex) { Debug.LogError($"World.Apply({msg.GetType().Name}) threw: {ex}"); }
            finally { End(); }
        }

        /// <summary>Saniyelik: zaman eşikleri.</summary>
        public static void Tick()
        {
            Begin();
            try { TickPlayer(); }
            catch (Exception ex) { Debug.LogError($"World.Tick threw: {ex}"); }
            finally { End(); }
        }

        /// <summary>Logout / oyun değişimi: tüm modeller ve oturum türevi önbellekler (TimeSync, Config) düşer; olay üretilmez
        /// (sahne zaten kapanıyor). Dinleyiciler ve kablo (Connection) kalır.</summary>
        public static void ResetSession()
        {
            ResetTable(); ResetPlayer(); ResetSocial(); ResetLobby(); ResetConnection();
            TimeSync.Reset();
            Config.Reset();
            _dirty = WorldPart.None;
        }

        // ── iç: tur yönetimi ──────────────────────────────────────────────────────
        static void Begin() => _depth++;

        static void End()
        {
            if (--_depth > 0) return;
            var d = _dirty; _dirty = WorldPart.None;
            if (d == WorldPart.None) return;
            try { Changed?.Invoke(d); }
            catch (Exception ex) { Debug.LogError($"World.Changed subscriber threw: {ex}"); }
        }

        /// <summary>Anında, senkron dağıtım — kuyruk yok. Çağıran model yazımını BİTİRDİKTEN sonra Emit eder (değişmez).
        /// Dinleyici fırlatırsa model bozulmaz; ancak aynı olayın zincirde SONRAKİ dinleyicileri de o tur atlanır (invocation list
        /// alloc'suz bölünemez) — dinleyici fırlatmamalı.</summary>
        static void Emit(in WorldEvent e)
        {
            if (Trace) Debug.Log($"[world] {e}");
            try
            {
                if (Handlers.TryGetValue(e.Kind, out var h)) h?.Invoke(e);
                OnEvent?.Invoke(e);
            }
            catch (Exception ex) { Debug.LogError($"World event subscriber threw ({e.Kind}): {ex}"); }
        }
        static void Emit(Ev kind) => Emit(new WorldEvent(kind));
        static void Dirty(WorldPart p) => _dirty |= p;
    }
}
