// Masa yöneticisi (TileManager'ın DigitoyEngine karşılığı). Her yer bir slot numarası; item o numaraya gider.
//   Havuz: AllocItem/FreeItem — item asla yok edilmez; FreeItem yalnız sunucu kaynaklı Set*/Remove/Clear'dan çağrılır (DnD'de yasak).
//   Yerleşim: CalculatePose(item) → zone geometrisi (item zone'un çocuğu); SetItemPose → tek Update döngüsü ease'ler.
//   Sync: SetSlot / SetRow / SetGrid / SetStack / Remove / Clear / RefreshLayout — idempotent, aynı No aynı nesne uçar.
//   GARANTİ: Stack dışı her slotta en çok bir item. Yerleştirme tek kapıdan (PlaceAt): dolu slot → kaydır / swap / ret; asla üst üste,
//   asla kayıp. Her LayoutZone sonunda Validate: çakışma varsa fazlalık ilk boşa alınır ve hata loglanır.
//   DnD (restricted, hedef listeli): sürükleme başında oyun OnDragBegin ile gidilebilecek hedefleri verir
//   (DropTarget.Zone = bölgenin tamamı, canlı hole/insert önizlemesiyle; DropTarget.Cell = tek hücre). Base her hareket en yakın hedefi
//   seçer → OnDragHover (oyun hilite). Bırakma: Zone hedefi → TryFitToRow (TileManager hole), Stack hedefi → OnThrow, Cell hedefi →
//   OnPlace (oyun model+action; swap taşı için slot verir). Ret → SavedPose.
using System;
using System.Collections.Generic;
using DigitoyEngine;

namespace Digitoy
{
    /// <summary>Sürükleme hedefi: bölgenin tamamı (hole/insert) ya da tek hücre (kesin yer).</summary>
    public struct DropTarget
    {
        public int slot;                 // Cell: hücre slotu · Zone: bölgenin firstSlot'u
        public bool wholeZone;
        public int tag;                  // oyunun kendi bilgisi (meldIndex vb.)
        public static DropTarget Cell(int slot, int tag = 0) => new DropTarget { slot = slot, wholeZone = false, tag = tag };
        public static DropTarget Zone(BoardGameZone z, int tag = 0) => new DropTarget { slot = z.firstSlot, wholeZone = true, tag = tag };
    }

    /// <summary>OnPlace sonucu: kabul + (varsa) hedefteki mevcut item'ın gideceği slot (okey alma: joker ıstakaya).</summary>
    public struct PlaceResult
    {
        public bool accept;
        public int swapTo;               // -1 = hedef boş olmalı / swap yok
        public static PlaceResult Reject => new PlaceResult { accept = false, swapTo = -1 };
        public static PlaceResult Accept(int swapTo = -1) => new PlaceResult { accept = true, swapTo = swapTo };
    }

    public abstract class BoardGameManager<T> : Component where T : BoardGameItem<T>
    {
        public const int ZONE_MY_HAND = 0;
        public const int ZONE_DISCARD0 = 100;
        public const int ZONE_DISCARD1 = 200;
        public const int ZONE_DISCARD2 = 300;
        public const int ZONE_DISCARD3 = 400;
        public const int ZONE_INDICATOR = 500;
        public const int ZONE_DECK = 1000;

        public Prefab ItemPrefab;
        public float moveDuration = 0.25f;
        /// <summary>Sürüklenen item'ın parmağın üstünde durma payı (px, yukarı).</summary>
        public float dragLift = 30f;
        /// <summary>Basılı tutma süresi (s) → çoklu seçim (zone.multiSelect).</summary>
        public float multiSelectDelay = 0.35f;
        /// <summary>Cell hedefine "isabet" yarıçapı, hücre boyunun katı.</summary>
        public float cellSnap = 0.7f;

        readonly List<T> _pool = new();
        readonly List<T> _items = new();
        readonly List<BoardGameZone> _zones = new();
        readonly List<T> _dragging = new();
        readonly List<T> _row = new();
        readonly List<T> _tmp = new();
        readonly List<DropTarget> _targets = new();
        readonly Dictionary<int, int> _stackCount = new();
        static readonly List<int> Empty = new();

        Vec3 _dragDelta;
        float _pressTime;
        T _pressed;
        int _hoverIndex = -1;            // _targets içindeki aktif hedef (-1 yok)
        int _lastPreviewSlot = -1;
        bool _dragCompact;

        public IReadOnlyList<T> Items => _items;
        public IReadOnlyList<BoardGameZone> Zones => _zones;
        public IReadOnlyList<T> Dragging => _dragging;
        public IReadOnlyList<DropTarget> Targets => _targets;
        public bool IsDragging => _dragging.Count > 0;
        /// <summary>Aktif hedef (yoksa slot = -1).</summary>
        public DropTarget Hover => _hoverIndex >= 0 ? _targets[_hoverIndex] : new DropTarget { slot = -1 };

        protected override void Awake()
        {
            _zones.Clear();
            CollectZones(transform);
        }

        void CollectZones(Transform t)
        {
            for (var i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                var z = c.gameObject.GetComponent<BoardGameZone>();
                if (z != null) _zones.Add(z);
                CollectZones(c);
            }
        }

        // ── oyun kancaları ───────────────────────────────────────────────────────
        /// <summary>Sürükleme başlayabilir mi (sıra bende mi; desteden çekerken No ata).</summary>
        protected virtual bool CanDrag(T item) => true;
        /// <summary>Sürükleme başladı: gidilebilecek hedefleri doldur. Varsayılan: Row/Grid bölgelerin tamamı + Stack'ler (serbest).</summary>
        protected virtual void OnDragBegin(IReadOnlyList<T> items, List<DropTarget> targets)
        {
            foreach (var z in _zones) targets.Add(DropTarget.Zone(z));
        }
        /// <summary>Aktif hedef değişti (hilite). target.slot = -1 → hedef yok.</summary>
        protected virtual void OnDragHover(DropTarget target) { }
        /// <summary>Stack hedefe bırakıldı (ıskarta/deste/gösterge). true = kabul (item.CurrentPose.Slot = targetSlot; türev değiştirebilir).</summary>
        protected virtual bool OnThrow(T item, int targetSlot) => true;
        /// <summary>Row/Grid (Zone hedefi) dışından ele/ıstakaya alındı (ıskarta/deste/per). false = geri.</summary>
        protected virtual bool OnPick(T item, int sourceSlot) => true;
        /// <summary>Cell hedefe bırakıldı: oyun model+action; swapTo = hedefteki mevcut item'ın gideceği slot.</summary>
        protected virtual PlaceResult OnPlace(IReadOnlyList<T> items, DropTarget target) => PlaceResult.Reject;
        protected virtual void OnItemTapped(T item) { }
        protected virtual void OnDragging() { }

        // ── havuz ────────────────────────────────────────────────────────────────
        public T AllocItem(int no)
        {
            T item;
            if (_pool.Count > 0) { item = _pool[^1]; _pool.RemoveAt(_pool.Count - 1); }
            else
            {
                item = ItemPrefab.Instantiate(transform).GetComponent<T>();
                item.Manager = this;
            }
            item.No = no; item.Depth = 0; item.Dragging = false; item.MoveDuration = 0f;
            item.CurrentPose.Slot = -1; item.CurrentPose.Parent = null; item.SavedPose.Slot = -1;
            item.gameObject.SetActive(true);
            _items.Add(item);
            return item;
        }

        public void FreeItem(T item)
        {
            if (item == null) return;
            if (item.Dragging) { Debug.LogError($"BoardGame: sürüklenen item serbest bırakılamaz (No {item.No})"); return; }
            item.MoveDuration = 0f;
            item.CurrentPose.Slot = -1; item.SavedPose.Slot = -1;
            item.gameObject.SetActive(false);
            _items.Remove(item);
            _pool.Add(item);
        }

        // ── sorgular ─────────────────────────────────────────────────────────────
        public BoardGameZone ZoneOf(int slot)
        {
            foreach (var z in _zones) if (z.Contains(slot)) return z;
            return null;
        }

        public T FindByNo(int no)
        {
            if (no < 0) return null;
            foreach (var it in _items) if (it.No == no) return it;
            return null;
        }

        /// <summary>Slottaki item (yığında tepe); sürüklenenler sayılmaz.</summary>
        public T FindBySlot(int slot)
        {
            T top = null;
            foreach (var it in _items) if (it.CurrentPose.Slot == slot && !it.Dragging && (top == null || it.Depth > top.Depth)) top = it;
            return top;
        }

        public int CountInZone(BoardGameZone z)
        {
            var n = 0;
            foreach (var it in _items) if (!it.Dragging && z.Contains(it.CurrentPose.Slot)) n++;
            return n;
        }

        public int CountAt(int slot) { var n = 0; foreach (var it in _items) if (it.CurrentPose.Slot == slot) n++; return n; }
        public int StackCount(int slot) => _stackCount.TryGetValue(slot, out var n) ? n : 0;

        /// <summary>Bölgedeki No'lar slot sırasıyla (diziliş gönder).</summary>
        public void CollectNos(BoardGameZone z, List<int> into)
        {
            Collect(z, _tmp); into.Clear();
            foreach (var it in _tmp) into.Add(it.No);
        }

        void Collect(BoardGameZone z, List<T> into)
        {
            into.Clear();
            foreach (var it in _items) if (z.Contains(it.CurrentPose.Slot)) into.Add(it);
            into.Sort(static (a, b) => a.CurrentPose.Slot != b.CurrentPose.Slot ? a.CurrentPose.Slot.CompareTo(b.CurrentPose.Slot) : a.Depth.CompareTo(b.Depth));
        }

        /// <summary>Boş slot: istenen boşsa o; Grid'de sağ-alttan ilk boş (TileManager FindBoardSlot); Row'da son. -1 = yer yok.</summary>
        public int FindFreeSlot(BoardGameZone z, int requested = -1)
        {
            if (requested >= 0 && z.Contains(requested) && FindBySlot(requested) == null) return requested;
            if (z.layout == ZoneLayout.Row) { var n = CountInZone(z); return n < z.slotCount ? z.firstSlot + n : -1; }
            for (var s = z.LastSlot; s >= z.firstSlot; s--) if (FindBySlot(s) == null) return s;
            return -1;
        }

        /// <summary>Grid hücresi için satır/sütun (oyun hedef hesaplarında).</summary>
        public static void RowCol(BoardGameZone z, int slot, out int row, out int col)
        {
            var i = slot - z.firstSlot; var c = Math.Max(1, z.columns);
            row = i / c; col = i % c;
        }

        // ── sync (sunucu → görsel) ──────────────────────────────────────────────
        /// <summary>No'lu item'ı slota koy (yoksa fromSlot'tan doğar). Dolu Row/Grid slotunda PlaceAt kuralı (kaydırma) işler.</summary>
        public T SetSlot(int no, int slot, bool animated = true, int fromSlot = -1)
        {
            var z = ZoneOf(slot);
            if (z == null) return null;
            var it = FindByNo(no) ?? Spawn(no, fromSlot);
            var from = ZoneOf(it.CurrentPose.Slot);
            PlaceAt(it, slot);
            if (from != null && from != z) LayoutZone(from, animated);
            LayoutZone(z, animated);
            return it;
        }

        /// <summary>Ardışık slotlara No'ları diz (Row: tüm bölge; Grid: slotun satırı, slottan itibaren). Aralıkta olup listede olmayan gider. İdempotent.</summary>
        public void SetRow(int firstSlot, IReadOnlyList<int> nos, bool animated = true, int fromSlot = -1)
        {
            var z = ZoneOf(firstSlot);
            if (z == null) return;
            z.RowRange(firstSlot, out var a, out var b);
            if (z.layout == ZoneLayout.Grid) a = firstSlot;
            nos ??= Empty;
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i]; var s = it.CurrentPose.Slot;
                if (s >= a && s <= b && !it.Dragging && IndexOf(nos, it.No) < 0) FreeItem(it);
            }
            _tmpZones.Clear();
            for (var k = 0; k < nos.Count && a + k <= b; k++)
            {
                var it = FindByNo(nos[k]) ?? Spawn(nos[k], fromSlot);
                var from = ZoneOf(it.CurrentPose.Slot);
                if (from != null && from != z && !_tmpZones.Contains(from)) _tmpZones.Add(from);
                Assign(it, a + k, 0);
            }
            foreach (var oz in _tmpZones) LayoutZone(oz, animated);
            _tmpZones.Clear();
            LayoutZone(z, animated);
        }

        /// <summary>Grid bölgeyi No listesiyle uzlaştır: mevcutlar yerinde kalır, listede olmayan gider, yeniler boş hücreye (sağ-alttan).</summary>
        public void SetGrid(BoardGameZone z, IReadOnlyList<int> nos, bool animated = true, int fromSlot = -1)
        {
            nos ??= Empty;
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (z.Contains(it.CurrentPose.Slot) && !it.Dragging && IndexOf(nos, it.No) < 0) FreeItem(it);
            }
            foreach (var no in nos)
            {
                var it = FindByNo(no);
                if (it != null && z.Contains(it.CurrentPose.Slot)) continue;
                var slot = FindFreeSlot(z);
                if (slot < 0) { Debug.LogError($"BoardGame: {z.name} dolu, No {no} yerleşemedi"); break; }
                it ??= Spawn(no, fromSlot);
                var from = ZoneOf(it.CurrentPose.Slot);
                Assign(it, slot, 0);
                if (from != null && from != z) LayoutZone(from, animated);
            }
            LayoutZone(z, animated);
        }

        /// <summary>Yığın: adet + görünür tepe No'ları (boş = hepsi kapalı). En çok maxVisible item çizilir.</summary>
        public void SetStack(int slot, IReadOnlyList<int> topNos, int count, bool animated = true, int fromSlot = -1)
        {
            var z = ZoneOf(slot);
            if (z == null || z.layout != ZoneLayout.Stack) return;
            topNos ??= Empty;
            _stackCount[slot] = count;
            var vis = Math.Min(count, z.maxVisible);
            var known = Math.Min(vis, topNos.Count);
            var closedNeeded = vis - known;
            var closedHave = 0;
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (it.CurrentPose.Slot != slot || it.Dragging) continue;
                if (it.No < 0) { if (closedHave < closedNeeded) closedHave++; else FreeItem(it); }
                else if (IndexOf(topNos, it.No) < topNos.Count - known) FreeItem(it);
            }
            var depth = 0;
            for (var i = 0; i < closedNeeded; i++)
            {
                var it = i < closedHave ? NthClosed(slot, i) : Spawn(-1, fromSlot);
                Assign(it, slot, depth++);
            }
            for (var k = topNos.Count - known; k < topNos.Count; k++)
            {
                var it = FindByNo(topNos[k]) ?? Spawn(topNos[k], fromSlot);
                var from = ZoneOf(it.CurrentPose.Slot);
                Assign(it, slot, depth++);
                if (from != null && from != z) LayoutZone(from, animated);
            }
            LayoutZone(z, animated);
        }

        T NthClosed(int slot, int n)
        {
            var k = 0;
            foreach (var it in _items) if (it.CurrentPose.Slot == slot && it.No < 0) { if (k == n) return it; k++; }
            return Spawn(-1, -1);
        }

        public void Remove(int no)
        {
            var it = FindByNo(no);
            if (it == null) return;
            var z = ZoneOf(it.CurrentPose.Slot);
            FreeItem(it);
            if (z != null) LayoutZone(z, true);
        }

        public void Clear()
        {
            if (IsDragging) StopDragging();
            for (var i = _items.Count - 1; i >= 0; i--) FreeItem(_items[i]);
            _stackCount.Clear();
        }

        public void RefreshLayout(bool animated = false)
        {
            foreach (var z in _zones) LayoutZone(z, animated);
        }

        T Spawn(int no, int fromSlot)
        {
            var it = AllocItem(no);
            var fz = fromSlot >= 0 ? ZoneOf(fromSlot) : null;
            if (fz != null)
            {
                // çıkış yeri: kaynak yığının tepesi — anında oraya koy, Layout hedefe uçurur
                it.Depth = Math.Max(0, StackCount(fromSlot) - 1);
                it.CurrentPose.Slot = fromSlot;
                CalculatePose(it);
                ApplyPoseNow(it);
                it.CurrentPose.Slot = -1;
            }
            else it.CurrentPose.Parent = null;   // hedefte belirir
            return it;
        }

        // ── yerleştirme (tek kapı) ───────────────────────────────────────────────
        /// <summary>Ham atama: slot/derinlik/closed/compact; eski yığından ayrılıyorsa yeniden dizer. Çakışma kontrolü YOK (Set* kendi uzlaştırır).</summary>
        void Assign(T it, int slot, int depth)
        {
            var old = it.CurrentPose.Slot;
            var z = ZoneOf(slot);
            it.CurrentPose.Slot = slot; it.Depth = depth;
            it.CurrentPose.Closed = z.closed || it.No < 0;
            it.CurrentPose.Compact = z.compact;
            if (old >= 0 && old != slot) { var oz = ZoneOf(old); if (oz != null && oz.layout == ZoneLayout.Stack) RestackAfterLeave(old); }
        }

        /// <summary>Item'ı slota koy; Stack → üste; Row/Grid dolu → mevcut sahibi aynı satırda ilk boşa kaydır (yer yoksa ret → false).</summary>
        bool PlaceAt(T it, int slot)
        {
            var z = ZoneOf(slot);
            if (z == null) return false;
            if (z.layout == ZoneLayout.Stack)
            {
                var n = StackCount(slot);
                _stackCount[slot] = n + 1;
                Assign(it, slot, n);
                return true;
            }
            var occ = FindBySlot(slot);
            if (occ != null && occ != it)
            {
                var free = FindFreeSlot(z);
                if (free < 0) return false;
                Assign(occ, free, 0);
            }
            Assign(it, slot, 0);
            return true;
        }

        void RestackAfterLeave(int slot)
        {
            var d = 0;
            Collect(ZoneOf(slot), _tmp);
            foreach (var it in _tmp) if (it.CurrentPose.Slot == slot) it.Depth = d++;
        }

        // ── yerleşim ─────────────────────────────────────────────────────────────
        /// <summary>Bölgedeki her item'ı slot pose'una götür. Sürükleme yoksa Saved=Current. Sonunda Validate.</summary>
        public void LayoutZone(BoardGameZone z, bool animated)
        {
            if (z.layout == ZoneLayout.Row) CompactRow(z);
            Validate(z);
            Collect(z, _tmp);
            var settle = _dragging.Count == 0;
            foreach (var it in _tmp)
            {
                if (it.Dragging) continue;
                CalculatePose(it);
                it.transform.SetAsLastSibling();
                SetItemPose(it, animated);
                if (settle) it.SavePose();
                it.Decorate();
            }
        }

        /// <summary>Row: bitişik yeniden numarala (ayrılanın boşluğu kapanır). Saved'e dokunmaz (önizleme referansı).</summary>
        void CompactRow(BoardGameZone z)
        {
            _tmp.Clear();
            foreach (var it in _items) if (!it.Dragging && z.Contains(it.CurrentPose.Slot)) _tmp.Add(it);
            _tmp.Sort(static (x, y) => x.CurrentPose.Slot.CompareTo(y.CurrentPose.Slot));
            for (var i = 0; i < _tmp.Count; i++) _tmp[i].CurrentPose.Slot = z.firstSlot + i;
        }

        /// <summary>GARANTİ: Stack dışı bölgede aynı slotta iki item olamaz. Olursa fazlalık ilk boşa, yoksa Row sonuna; hata loglanır.</summary>
        void Validate(BoardGameZone z)
        {
            if (z.layout == ZoneLayout.Stack) return;
            Collect(z, _tmp);
            for (var i = 1; i < _tmp.Count; i++)
            {
                var a = _tmp[i - 1]; var b = _tmp[i];
                if (a.Dragging || b.Dragging || a.CurrentPose.Slot != b.CurrentPose.Slot) continue;
                var free = FindFreeSlot(z);
                Debug.LogError($"BoardGame: {z.name} slot {b.CurrentPose.Slot} çakışma (No {a.No}/{b.No}) → {free}");
                if (free >= 0) { Assign(b, free, 0); Collect(z, _tmp); i = 0; }
            }
        }

        /// <summary>Slot → CurrentPose (konum/rotasyon/ölçek/parent/closed/compact). Zone geometrisi; türev ezebilir.</summary>
        public virtual void CalculatePose(T it)
        {
            var slot = it.CurrentPose.Slot;
            var z = ZoneOf(slot);
            if (z == null) return;
            var ctx = new BoardGameZone.Ctx();
            switch (z.layout)
            {
                case ZoneLayout.Row: ctx.count = CountInZone(z); break;
                case ZoneLayout.Stack: ctx.count = Math.Max(StackCount(slot), CountAt(slot)); ctx.depth = it.Depth; break;
            }
            var pos = z.CalculatePosition(slot, ctx, out var rot);
            var nat = ItemNaturalSize(z.compact);
            it.CurrentPose.Position = pos;
            it.CurrentPose.Rotation = rot;
            it.CurrentPose.Scale = new Vec3(z.itemSize.x / nat.x, z.itemSize.y / nat.y, 1f);
            it.CurrentPose.Parent = z.gameObject;
            it.CurrentPose.Closed = z.closed || it.No < 0;
            it.CurrentPose.Compact = z.compact;
        }

        /// <summary>Bir slotun pose'u (hayalet/hilite için) — item'a dokunmadan.</summary>
        public void PoseOfSlot(int slot, out Vec3 position, out Vec3 rotation, out Vec3 scale, out GameObject parent)
        {
            var z = ZoneOf(slot);
            position = default; rotation = default; scale = new Vec3(1, 1, 1); parent = null;
            if (z == null) return;
            var ctx = new BoardGameZone.Ctx { count = z.layout == ZoneLayout.Row ? CountInZone(z) + 1 : Math.Max(1, StackCount(slot)), depth = Math.Max(0, StackCount(slot) - 1) };
            position = z.CalculatePosition(slot, ctx, out rotation);
            var nat = ItemNaturalSize(z.compact);
            scale = new Vec3(z.itemSize.x / nat.x, z.itemSize.y / nat.y, 1f);
            parent = z.gameObject;
        }

        /// <summary>Prefab'ın doğal boyu (ölçek 1'de; compact = kompakt sprite boyu); türev verir.</summary>
        protected virtual Vec2 ItemNaturalSize(bool compact) => new Vec2(140f, 202f);

        /// <summary>CurrentPose'a git: parent değişirse dünya duruşu korunarak yeniden bağlanır, sonra ease.</summary>
        public void SetItemPose(T it, bool animated, float delay = 0f)
        {
            var p = it.CurrentPose;
            var t = it.transform;
            var parent = p.Parent != null ? p.Parent.transform : transform;
            if (!ReferenceEquals(t.parent, parent)) t.SetParent(parent, true);
            if (!animated || moveDuration <= 0f) { ApplyPoseNow(it); return; }
            it.FromPos = t.localPosition; it.FromRot = t.localEulerAngles; it.FromScale = t.localScale;
            it.MoveStart = Time.time; it.MoveDuration = moveDuration; it.MoveDelay = delay; it.MoveEventCalled = false;
        }

        void ApplyPoseNow(T it)
        {
            var p = it.CurrentPose; var t = it.transform;
            var parent = p.Parent != null ? p.Parent.transform : transform;
            if (!ReferenceEquals(t.parent, parent)) t.SetParent(parent, false);
            t.localPosition = p.Position; t.localEulerAngles = p.Rotation; t.localScale = p.Scale;
            it.MoveDuration = 0f;
        }

        protected override void Update()
        {
            var i = 0;
            while (i < _items.Count)
            {
                var it = _items[i];
                if (!it.Dragging && it.MoveDuration > 0f)
                {
                    var time = Time.time - it.MoveStart - it.MoveDelay;
                    if (time < 0f) { i++; continue; }
                    if (!it.MoveEventCalled) { it.MoveEventCalled = true; it.OnStartedToMove(); }
                    var t = it.transform; var p = it.CurrentPose;
                    if (time >= it.MoveDuration)
                    {
                        t.localPosition = p.Position; t.localEulerAngles = p.Rotation; t.localScale = p.Scale;
                        it.MoveDuration = 0f;
                        if (it.OnArrived()) { FreeItem(it); continue; }
                    }
                    else
                    {
                        var k = time / it.MoveDuration;
                        k = 1f - (1f - k) * (1f - k) * (1f - k);          // OutCubic
                        t.localPosition = Lerp(it.FromPos, p.Position, k);
                        t.localEulerAngles = LerpAngles(it.FromRot, p.Rotation, k);
                        t.localScale = Lerp(it.FromScale, p.Scale, k);
                    }
                }
                i++;
            }
            if (_pressed != null && !IsDragging && _pressTime > 0f && Time.time - _pressTime >= multiSelectDelay) CheckMultiSelect();
        }

        // ── DnD ──────────────────────────────────────────────────────────────────
        internal void OnItemPointerDown(T it, PointerEvent e)
        {
            _pressed = it; _pressTime = Time.time;
        }

        internal void OnItemPointerUp(T it, PointerEvent e)
        {
            _pressed = null; _pressTime = 0f;
            if (_dragging.Count > 0) { Drop(); e.Use(); }
        }

        internal void OnItemClick(T it, PointerEvent e)
        {
            if (it.CurrentPose.Slot >= 0) { OnItemTapped(it); e.Use(); }
        }

        internal void OnItemDragStart(T it, PointerEvent e)
        {
            if (_dragging.Count > 0) { if (_dragging.Contains(it)) e.Use(); return; }
            _pressTime = 0f;
            if (it.CurrentPose.Slot < 0 || !CanDrag(it)) return;
            StartDragging(it);
            BeginTargets();
            e.Use();
        }

        internal void OnItemDrag(T it, PointerEvent e)
        {
            if (_dragging.Count == 0 || !_dragging.Contains(it)) return;
            var lead = _dragging[0];
            var d = e.TotalInParentOf(transform);
            var lp = new Vec3(_dragDelta.x + d.x, _dragDelta.y + d.y - dragLift, _dragDelta.z);
            var step = DragStep(lead);
            for (var i = 0; i < _dragging.Count; i++)
                _dragging[i].transform.localPosition = new Vec3(lp.x + i * step, lp.y, lp.z);
            UpdateHover();
            Preview();
            OnDragging();
            e.Use();
        }

        float DragStep(T lead) => lead.transform.localScale.x * ItemNaturalSize(lead.CurrentPose.Compact).x;

        void StartDragging(T it)
        {
            _dragging.Clear();
            _dragging.Add(it);
            it.Dragging = true;
            it.MoveDuration = 0f;
            it.SavePose();
            it.transform.SetParent(transform, true);           // sürükleme manager uzayında
            it.transform.SetAsLastSibling();
            _dragDelta = it.transform.localPosition;
            _lastPreviewSlot = -1; _hoverIndex = -1; _dragCompact = it.CurrentPose.Compact;
            var z = ZoneOf(it.CurrentPose.Slot);
            if (z != null && z.layout == ZoneLayout.Stack) { it.CurrentPose.Slot = -1; RestackAfterLeave(it.SavedPose.Slot); LayoutZone(z, true); it.CurrentPose.Slot = it.SavedPose.Slot; }
        }

        void BeginTargets()
        {
            _targets.Clear();
            OnDragBegin(_dragging, _targets);
            UpdateHover();
        }

        /// <summary>Basılı tutma: aynı satırdaki bitişik item'lar gruba (TileManager CheckMultiSelect).</summary>
        void CheckMultiSelect()
        {
            var first = _pressed; _pressTime = 0f;
            var z = ZoneOf(first.CurrentPose.Slot);
            if (z == null || !z.multiSelect || z.layout != ZoneLayout.Grid || !CanDrag(first)) return;
            StartDragging(first);
            z.RowRange(first.CurrentPose.Slot, out var a, out var b);
            var s0 = first.CurrentPose.Slot;
            for (var s = s0 - 1; s >= a; s--) { var it = FindBySlot(s); if (it == null) break; _dragging.Insert(0, it); }
            for (var s = s0 + 1; s <= b; s++) { var it = FindBySlot(s); if (it == null) break; _dragging.Add(it); }
            if (_dragging.Count > 1)
            {
                var idx = _dragging.IndexOf(first);
                var step = DragStep(first);
                _dragDelta = new Vec3(_dragDelta.x - idx * step, _dragDelta.y, _dragDelta.z);
                for (var i = 0; i < _dragging.Count; i++)
                {
                    var it = _dragging[i];
                    if (it != first) { it.Dragging = true; it.MoveDuration = 0f; it.SavePose(); it.transform.SetParent(transform, true); }
                    it.transform.SetAsLastSibling();
                    it.transform.localPosition = new Vec3(_dragDelta.x + i * step, first.transform.localPosition.y, first.transform.localPosition.z);
                    it.OnMultiSelect();
                }
            }
            BeginTargets();
        }

        /// <summary>Sürüklenen grubu kompakt görsele al/çıkar (meld alanı üstünde küçülme). Oyun OnDragHover'da çağırır; ölçek = itemSize / doğal boy.</summary>
        public void SetDragCompact(bool compact, Vec2 itemSize)
        {
            if (_dragCompact == compact || _dragging.Count == 0) return;
            _dragCompact = compact;
            var nat = ItemNaturalSize(compact);
            var sc = new Vec3(itemSize.x / nat.x, itemSize.y / nat.y, 1f);
            foreach (var it in _dragging)
            {
                it.CurrentPose.Compact = compact;
                it.transform.localScale = sc;
                it.Decorate();
            }
        }

        // ── hedef seçimi ─────────────────────────────────────────────────────────
        /// <summary>Lider konumuna göre en yakın hedefi seç; değiştiyse OnDragHover.</summary>
        void UpdateHover()
        {
            var best = -1; var bestD = float.MaxValue;
            for (var i = 0; i < _targets.Count; i++)
            {
                var t = _targets[i];
                var z = ZoneOf(t.slot);
                if (z == null) continue;
                if (t.wholeZone)
                {
                    // grubun herhangi bir item'ı bölgedeyse aday (mesafe = bölge merkezine, bölge hedefleri hücreden sonra gelsin diye +büyük)
                    for (var k = 0; k < _dragging.Count; k++)
                    {
                        var q = _dragging[k].transform.localPosition;
                        if (ZoneLocalSlot(z, new Vec2(q.x, q.y)) >= 0) { var d = 1e6f + k; if (d < bestD) { bestD = d; best = i; } break; }
                    }
                }
                else
                {
                    var lp = _dragging[0].transform.localPosition;
                    var cell = CellCenterManager(z, t.slot);
                    var dx = lp.x - cell.x; var dy = lp.y - cell.y;
                    var d = MathF.Sqrt(dx * dx + dy * dy);
                    var sc = z.transform.localScale.x;
                    var r = cellSnap * Math.Max(z.itemSize.x, z.itemSize.y) * (sc == 0 ? 1 : sc);
                    if (d <= r && d < bestD) { bestD = d; best = i; }
                }
            }
            if (best != _hoverIndex)
            {
                _hoverIndex = best;
                OnDragHover(Hover);
            }
        }

        /// <summary>Zone hedefi (Row/Grid) üzerinde canlı hole/insert önizlemesi.</summary>
        void Preview()
        {
            var h = Hover;
            var z = h.slot >= 0 ? ZoneOf(h.slot) : null;
            var target = -1;
            if (z != null && h.wholeZone && z.layout != ZoneLayout.Stack)
                for (var k = 0; k < _dragging.Count && target < 0; k++) { var q = _dragging[k].transform.localPosition; target = ZoneLocalSlot(z, new Vec2(q.x, q.y)); }
            if (target == _lastPreviewSlot) return;
            _lastPreviewSlot = target;
            var home = ZoneOf(_dragging[0].SavedPose.Slot);
            RestoreAll();
            if (target >= 0) TryFitToRow(z, target, false);
            else if (home != null) LayoutZone(home, true);
        }

        /// <summary>Bırakma: aktif hedefe göre OnThrow / OnPlace / TryFitToRow; ret → SavedPose.</summary>
        void Drop()
        {
            var lead = _dragging[0];
            var home = ZoneOf(lead.SavedPose.Slot);
            var h = Hover;
            var z = h.slot >= 0 ? ZoneOf(h.slot) : null;
            var accepted = false;

            RestoreAll();
            if (z != null)
            {
                if (h.wholeZone && z.layout == ZoneLayout.Stack)
                {
                    if (_dragging.Count == 1)
                    {
                        lead.CurrentPose.Slot = h.slot;
                        if (OnThrow(lead, h.slot)) { accepted = PlaceAt(lead, lead.CurrentPose.Slot); }
                        if (!accepted) lead.RestorePose();
                    }
                }
                else if (h.wholeZone)
                {
                    var target = -1;
                    for (var k = 0; k < _dragging.Count && target < 0; k++) { var q = _dragging[k].transform.localPosition; target = ZoneLocalSlot(z, new Vec2(q.x, q.y)); }
                    if (target >= 0 && TryFitToRow(z, target, true))
                    {
                        accepted = true;
                        if (home != z && home != null && home.layout == ZoneLayout.Stack && !OnPick(lead, lead.SavedPose.Slot)) accepted = false;
                    }
                    if (!accepted) foreach (var it in _dragging) it.RestorePose();
                }
                else
                {
                    var r = OnPlace(_dragging, h);
                    if (r.accept)
                    {
                        // Cell: grup yan yana (aynı satırda); hedefteki mevcut item swapTo'ya (yoksa aynı bölgede ilk boşa)
                        accepted = true;
                        for (var k = 0; k < _dragging.Count; k++)
                        {
                            var slot = h.slot + k;
                            var occ = FindBySlot(slot);
                            if (occ != null)
                            {
                                var to = r.swapTo >= 0 && FindBySlot(r.swapTo) == null ? r.swapTo : FindFreeSlot(ZoneOf(r.swapTo >= 0 ? r.swapTo : slot));
                                if (to < 0) { accepted = false; break; }
                                var oz = ZoneOf(occ.CurrentPose.Slot);
                                Assign(occ, to, 0);
                                var nz = ZoneOf(to);
                                if (nz != oz) LayoutZone(oz, true);
                                LayoutZone(nz, true);
                            }
                            Assign(_dragging[k], slot, 0);
                        }
                        if (!accepted) foreach (var it in _dragging) it.RestorePose();
                    }
                    else foreach (var it in _dragging) it.RestorePose();
                }
            }
            else foreach (var it in _dragging) it.RestorePose();

            StopDragging(home);
        }

        void StopDragging(BoardGameZone home = null)
        {
            foreach (var it in _dragging)
            {
                it.Dragging = false;
                var z = ZoneOf(it.CurrentPose.Slot);
                // ret ile yığına dönen item adedi geri alır
                if (z != null && z.layout == ZoneLayout.Stack && it.CurrentPose.Slot == it.SavedPose.Slot && it.Depth >= StackCount(it.CurrentPose.Slot))
                    _stackCount[it.CurrentPose.Slot] = it.Depth + 1;
            }
            _tmpZones.Clear();
            foreach (var it in _dragging) { var z = ZoneOf(it.CurrentPose.Slot); if (z != null && !_tmpZones.Contains(z)) _tmpZones.Add(z); }
            _dragging.Clear();
            _targets.Clear();
            _hoverIndex = -1; _lastPreviewSlot = -1; _dragCompact = false;
            if (home != null && !_tmpZones.Contains(home)) _tmpZones.Add(home);
            foreach (var z in _tmpZones) LayoutZone(z, true);
            _tmpZones.Clear();
            OnDragHover(Hover);
        }

        readonly List<BoardGameZone> _tmpZones = new();

        /// <summary>Önizleme bozulsun: sürüklenmeyenler kaydedilmiş slotlarına (yalnız Row/Grid).</summary>
        void RestoreAll()
        {
            foreach (var it in _items)
            {
                if (it.Dragging) continue;
                var cz = ZoneOf(it.CurrentPose.Slot);
                if (cz == null || cz.layout == ZoneLayout.Stack) continue;
                if (it.SavedPose.Slot >= 0 && it.SavedPose.Slot != it.CurrentPose.Slot) { it.CurrentPose.Slot = it.SavedPose.Slot; CalculatePose(it); SetItemPose(it, true); }
            }
        }

        /// <summary>Sürüklenen grubu hedef satıra oturt: AddHole ile yer aç (TileManager TryFitToBoard). flush = kalıcı (Saved yazılır).</summary>
        bool TryFitToRow(BoardGameZone z, int target, bool flush)
        {
            z.RowRange(target, out var a, out var b);
            var width = b - a + 1;
            _row.Clear(); for (var i = 0; i < width; i++) _row.Add(null);
            foreach (var it in _items)
            {
                if (it.Dragging) continue;
                var s = it.SavedPose.Slot >= 0 && z.Contains(it.SavedPose.Slot) ? it.SavedPose.Slot : it.CurrentPose.Slot;
                if (s < a || s > b) continue;
                if (_row[s - a] != null) return false;
                _row[s - a] = it;
                it.CurrentPose.Slot = s;
            }
            var col = Math.Clamp(target - a, 0, Math.Max(0, width - _dragging.Count));
            if (z.layout == ZoneLayout.Row)
            {
                var n = 0; foreach (var r in _row) if (r != null) n++;
                if (n + _dragging.Count > width) return false;
                _tmp.Clear(); foreach (var r in _row) if (r != null) _tmp.Add(r);
                col = Math.Min(col, _tmp.Count);
                _tmp.InsertRange(col, _dragging);
                for (var i = 0; i < _tmp.Count; i++) _tmp[i].CurrentPose.Slot = a + i;
            }
            else
            {
                for (var i = 0; i < _dragging.Count; i++)
                {
                    if (!AddHole(_row, col + i, i)) return false;
                    _row[col + i] = _dragging[i];
                }
                for (var i = 0; i < width; i++) if (_row[i] != null) _row[i].CurrentPose.Slot = a + i;
            }
            foreach (var it in _items)
            {
                if (it.Dragging || !z.Contains(it.CurrentPose.Slot)) continue;
                CalculatePose(it); SetItemPose(it, true);
                if (flush) it.SavePose();
            }
            if (flush) foreach (var it in _dragging) { Assign(it, it.CurrentPose.Slot, 0); it.SavePose(); }
            return true;
        }

        /// <summary>col'a boşluk aç: sağda boşluk varsa sağa kaydır, yoksa soldaki boşluğa doğru sola kaydır (TileManager AddHoleToRow).</summary>
        static bool AddHole(List<T> row, int col, int delta)
        {
            if (col < 0 || col >= row.Count) return false;
            if (row[col] == null) return true;
            var left = -1; for (var i = col - 1; i >= 0; i--) if (row[i] == null) { left = i; break; }
            var right = -1; for (var i = col + 1; i < row.Count; i++) if (row[i] == null) { right = i; break; }
            if (right != -1) { row.RemoveAt(right); row.Insert(col, null); return true; }
            if (left != -1)
            {
                for (var i = left + 1; i < col - delta; i++) row[i - 1] = row[i];
                row[col - 1 - delta] = row[col];
                row[col] = null;
                return true;
            }
            return false;
        }

        // ── koordinat ────────────────────────────────────────────────────────────
        /// <summary>Manager-yerel nokta → bu bölgede slot (−1 = dışında).</summary>
        int ZoneLocalSlot(BoardGameZone z, Vec2 local)
        {
            var o = ToManagerLocal(z.transform);
            var sc = z.transform.localScale;
            var l = new Vec2((local.x - o.x) / (sc.x == 0 ? 1 : sc.x), (local.y - o.y) / (sc.y == 0 ? 1 : sc.y));
            var ctx = new BoardGameZone.Ctx { count = CountInZone(z) };
            return z.SlotAt(l, ctx);
        }

        /// <summary>Manager-yerel nokta → slot (tüm bölgeler; −1 = boşluk).</summary>
        public int SlotAt(Vec2 local)
        {
            foreach (var z in _zones) { var s = ZoneLocalSlot(z, local); if (s >= 0) return s; }
            return -1;
        }

        Vec2 CellCenterManager(BoardGameZone z, int slot)
        {
            var ctx = new BoardGameZone.Ctx { count = CountInZone(z) + 1 };
            var p = z.CalculatePosition(slot, ctx, out _);
            var o = ToManagerLocal(z.transform); var sc = z.transform.localScale;
            return new Vec2(o.x + p.x * sc.x, o.y + p.y * sc.y);
        }

        Vec3 ToManagerLocal(Transform t)
        {
            var p = new Vec3(0, 0, 0);
            while (t != null && !ReferenceEquals(t, transform))
            {
                var lp = t.localPosition; var ls = t.parent != null ? t.parent.localScale : new Vec3(1, 1, 1);
                p = new Vec3(p.x * ls.x + lp.x, p.y * ls.y + lp.y, p.z + lp.z);
                t = t.parent;
            }
            return p;
        }

        static Vec3 Lerp(Vec3 a, Vec3 b, float k) => new Vec3(a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k, a.z + (b.z - a.z) * k);
        static Vec3 LerpAngles(Vec3 a, Vec3 b, float k) => new Vec3(LerpAngle(a.x, b.x, k), LerpAngle(a.y, b.y, k), LerpAngle(a.z, b.z, k));
        static float LerpAngle(float a, float b, float k) { var d = ((b - a) % 360f + 540f) % 360f - 180f; return a + d * k; }
        static int IndexOf(IReadOnlyList<int> l, int v) { for (var i = 0; i < l.Count; i++) if (l[i] == v) return i; return -1; }
    }
}
