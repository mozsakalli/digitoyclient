// Taş/kart: masadaki en küçük birim. Konum/rotasyon/ölçek hesaplamaz; manager CurrentPose'u yazar, Update döngüsü oraya ease'ler.
// SavedPose = sürükleme/önizleme öncesi hali (TileManager SaveType/RestoreType). Pointer olayları manager'a yönlendirilir.
// Görsel (yüz/arka/işaret) türevde (Card, Tile): manager Decorate ile çağırır.
using DigitoyEngine;

namespace Digitoy
{
    public abstract class BoardGameItem<T> : Component where T : BoardGameItem<T>
    {
        public class Pose
        {
            public int Slot { get; set; } = -1;
            public bool Closed { get; set; }
            /// <summary>Kompakt görsel (101 per/ıskarta küçük taşı); türev Decorate'te sprite seçer.</summary>
            public bool Compact { get; set; }
            public Vec3 Position { get; set; }
            public Vec3 Rotation { get; set; }
            public Vec3 Scale { get; set; } = new Vec3(1f, 1f, 1f);
            public GameObject Parent { get; set; }

            public void CopyFrom(Pose o)
            {
                Slot = o.Slot; Closed = o.Closed; Compact = o.Compact;
                Position = o.Position; Rotation = o.Rotation; Scale = o.Scale;
                Parent = o.Parent;
            }
        }

        public int No { get; set; } = -1;
        public bool Dragging { get; set; }
        /// <summary>Yığındaki sıra (0 = en alt); yığın dışı 0.</summary>
        public int Depth { get; set; }
        public Pose CurrentPose { get; } = new();
        public Pose SavedPose { get; } = new();

        public BoardGameManager<T> Manager { get; internal set; }

        // hareket (manager Update döngüsü)
        internal float MoveStart, MoveDuration, MoveDelay;
        internal Vec3 FromPos, FromRot, FromScale;
        internal bool MoveEventCalled;
        public bool Moving => MoveDuration > 0f;
        public bool IsBoardItem => CurrentPose.Slot >= 0 && CurrentPose.Slot < 100;

        public void SavePose() => SavedPose.CopyFrom(CurrentPose);
        public void RestorePose() => CurrentPose.CopyFrom(SavedPose);

        /// <summary>Türev: yüz/arka/işaret yenile (No, CurrentPose.Closed).</summary>
        public abstract void Decorate();
        /// <summary>Hareket bitti. true = manager havuza alsın.</summary>
        public virtual bool OnArrived() => false;
        public virtual void OnStartedToMove() { }
        public virtual void OnMultiSelect() { }

        protected override void OnPointerDown(PointerEvent e) { Manager?.OnItemPointerDown((T)this, e); }
        protected override void OnPointerUp(PointerEvent e) { Manager?.OnItemPointerUp((T)this, e); }
        protected override void OnPointerClick(PointerEvent e) { if (!e.dragged) Manager?.OnItemClick((T)this, e); }
        protected override void OnDragStart(PointerEvent e) { Manager?.OnItemDragStart((T)this, e); }
        protected override void OnDrag(PointerEvent e) { Manager?.OnItemDrag((T)this, e); }
    }
}
