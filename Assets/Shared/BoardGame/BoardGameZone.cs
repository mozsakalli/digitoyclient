// Bölge: masadaki bir YER aralığı (slot numaraları) + geometri. Item tutmaz; manager sorar:
//   CalculatePose(slot, ctx) → bölge-yerel konum/rotasyon/ölçek (item bu bölgenin çocuğu durur → bölgeyi zoom/kaydır, çocuklar bedava gelir)
//   SlotAt(local)            → tersi (bırakılan nokta hangi slot)
// Tipler: Row (el/yelpaze), Grid (ıstaka 2×13, 101 per ızgarası 13×13 — hücre = slot), Stack (deste/ıskarta: derinlik ofseti, maxVisible).
// Koordinat: bölge transform uzayı, merkez orijin, y AŞAĞI (sahne uzayı). Yalnız geometri; kural/hole/kaydırma manager'da.
using DigitoyEngine;

namespace Digitoy
{
    public enum ZoneLayout { Row, Grid, Stack }

    public class BoardGameZone : Component
    {
        public int firstSlot;
        public int slotCount = 1;
        public ZoneLayout layout = ZoneLayout.Row;
        /// <summary>Buradaki item'lar kapalı (deste, rakip eli).</summary>
        public bool closed;
        /// <summary>Buradaki item'lar kompakt görselli (101 per/ıskarta küçük taşı). itemSize kompakt boyu olmalı.</summary>
        public bool compact;
        /// <summary>Basılı tutunca satırdaki bitişikler birlikte sürüklenir (ıstaka).</summary>
        public bool multiSelect;
        /// <summary>Hit alanı (bölge-yerel, merkez orijin). 0 = itemSize'dan türet.</summary>
        public Vec2 size;
        /// <summary>Item'ın bu bölgedeki boyu (ölçek = itemSize / item doğal boyu — manager hesaplar).</summary>
        public Vec2 itemSize = new Vec2(140f, 202f);
        public Vec3 rotation;
        public Vec3 rotationJitter;
        public Vec2 positionJitter;

        // Row
        public float maxStep = 150f;
        /// <summary>Uç kartların z açısı (±derece), ortak pivotlu yay. 0 = düz.</summary>
        public float fanAngle;
        // Grid
        public int columns = 13;
        public int rows = 2;
        public float gap = 4f;
        // Stack
        public Vec3 stackOffset = new Vec3(0f, -2f, -1f);
        public int maxVisible = 3;

        public int LastSlot => firstSlot + slotCount - 1;
        public bool Contains(int slot) => slot >= firstSlot && slot <= LastSlot;

        public Vec2 Size => size.x > 0 && size.y > 0 ? size : layout switch
        {
            ZoneLayout.Grid => new Vec2(columns * (itemSize.x + gap), rows * (itemSize.y + gap)),
            ZoneLayout.Row => new Vec2(slotCount * itemSize.x, itemSize.y),
            _ => new Vec2(itemSize.x * 1.4f, itemSize.y * 1.4f),
        };

        /// <summary>Slotun ait olduğu bitişik dizi (hole/kaydırma aralığı): Row = bölge, Grid = satır, Stack = tek.</summary>
        public void RowRange(int slot, out int a, out int b)
        {
            var i = slot - firstSlot;
            switch (layout)
            {
                case ZoneLayout.Row: a = firstSlot; b = LastSlot; break;
                case ZoneLayout.Grid: a = firstSlot + i / System.Math.Max(1, columns) * columns; b = a + columns - 1; break;
                default: a = b = slot; break;
            }
        }

        /// <summary>Yerleşim bağlamı: Row'da dolu sayı; Stack'te derinlik/adet (manager doldurur).</summary>
        public struct Ctx
        {
            public int count;        // Row: dolu item sayısı · Stack: yığın adedi
            public int depth;        // Stack: 0 = en alt
        }

        public Vec3 CalculatePosition(int slot, in Ctx c, out Vec3 rot)
        {
            var i = slot - firstSlot;
            rot = rotation;
            Vec3 pos;
            switch (layout)
            {
                case ZoneLayout.Row:
                    {
                        var n = System.Math.Max(1, c.count);
                        var step = n <= 1 ? 0f : System.Math.Min(maxStep, (Size.x - itemSize.x) / (n - 1));
                        var x = (i - (n - 1) * 0.5f) * step;
                        var half = (n - 1) * 0.5f * step;
                        if (fanAngle != 0f && half > 0f)
                        {
                            var a = fanAngle * (System.Math.PI / 180.0);
                            var r = half / System.Math.Sin(a);
                            var th = System.Math.Asin(System.Math.Clamp(x / r, -1.0, 1.0));
                            pos = new Vec3(x, (float)(r - r * System.Math.Cos(th)), 0f);
                            rot = new Vec3(rotation.x, rotation.y, rotation.z - (float)(th * 180.0 / System.Math.PI));
                        }
                        else pos = new Vec3(x, 0f, 0f);
                        break;
                    }
                case ZoneLayout.Grid:
                    {
                        var cw = itemSize.x + gap; var ch = itemSize.y + gap;
                        var col = i % System.Math.Max(1, columns); var row = i / System.Math.Max(1, columns);
                        pos = new Vec3((col - (columns - 1) * 0.5f) * cw, (row - (rows - 1) * 0.5f) * ch, 0f);
                        break;
                    }
                default:
                    {
                        // tepe merkezde, altındakiler geriye (k ≤ 0), en çok maxVisible yayılır
                        var vis = System.Math.Min(System.Math.Max(1, c.count), maxVisible);
                        var k = System.Math.Max(c.depth - (c.count - 1), -(vis - 1));
                        pos = new Vec3(stackOffset.x * k, stackOffset.y * k, stackOffset.z * k);
                        i = c.depth;
                        break;
                    }
            }
            if (rotationJitter.x != 0 || rotationJitter.y != 0 || rotationJitter.z != 0 || positionJitter.x != 0 || positionJitter.y != 0)
            {
                float h1 = Hash(i * 7919 + 13), h2 = Hash(i * 104729 + 29), h3 = Hash(i * 1299709 + 31);
                rot = new Vec3(rot.x + rotationJitter.x * h1, rot.y + rotationJitter.y * h2, rot.z + rotationJitter.z * h3);
                pos = new Vec3(pos.x + positionJitter.x * h2, pos.y + positionJitter.y * h3, pos.z);
            }
            // yatık item düzleme gömülmesin (hit-test + görsel): dönüşten türeyen z kalkışı (kameraya doğru)
            var rx = System.Math.Abs(rot.x) * (System.Math.PI / 180.0);
            var lift = (float)(itemSize.y * 0.5 * System.Math.Sin(rx));
            if (lift > 0f) pos = new Vec3(pos.x, pos.y, pos.z - lift - 1f);
            return pos;
        }

        /// <summary>Bölge-yerel nokta → slot (Row: araya girme indeksi, Grid: hücre, Stack: tek). −1 = dışarıda.</summary>
        public int SlotAt(Vec2 local, in Ctx c)
        {
            var s = Size;
            // item'ın bir kısmı bölgeye değiyorsa sayılır (yarı item payı)
            if (System.Math.Abs(local.x) > s.x * 0.5f + itemSize.x * 0.5f || System.Math.Abs(local.y) > s.y * 0.5f + itemSize.y * 0.5f) return -1;
            switch (layout)
            {
                case ZoneLayout.Row:
                    {
                        var n = c.count;
                        if (n <= 0) return firstSlot;
                        var step = n <= 1 ? itemSize.x : System.Math.Min(maxStep, (s.x - itemSize.x) / (n - 1));
                        var i = (int)System.Math.Round(local.x / step + (n - 1) * 0.5f);
                        return firstSlot + System.Math.Clamp(i, 0, System.Math.Min(n, slotCount - 1));
                    }
                case ZoneLayout.Grid:
                    {
                        var cw = itemSize.x + gap; var ch = itemSize.y + gap;
                        var col = (int)System.Math.Floor(local.x / cw + columns * 0.5f);
                        var row = (int)System.Math.Floor(local.y / ch + rows * 0.5f);
                        col = System.Math.Clamp(col, 0, columns - 1); row = System.Math.Clamp(row, 0, rows - 1);
                        var slot = firstSlot + row * columns + col;
                        return slot <= LastSlot ? slot : -1;
                    }
                default:
                    return firstSlot;
            }
        }

        static float Hash(int n) { unchecked { n = (n ^ 61) ^ (n >> 16); n *= 9; n ^= n >> 4; n *= 0x27d4eb2d; n ^= n >> 15; } return ((n & 0xFFFF) / 32767.5f) - 1f; }
    }
}
