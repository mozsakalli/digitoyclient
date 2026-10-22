// 101 masası taslağı. Slotlar: ıstaka Grid 2×13 (0..25, çoklu seçim), gösterge 500, 4 ıskarta 100/200/300/400 (kompakt yığın),
// deste 1000 (kapalı), per ızgaraları: RUNSET_A 2000.. (13×13), RUNSET_B 2200.., TWIN_A 2400.. (2×13), TWIN_B 2500.. (Unity: row/col, col = rank).
// Kural yok (Common sonra): hedefler örnek — tek taş: her perin sol/sağ komşusu + kendi rengindeki joker; grup: ilk boş satırda rank kolonu.
using System.Collections.Generic;
using Digitoy;
using DigitoyEngine;

public class YuzbirGame : BoardGameManager<Tile>
{
    public const int RUNSET_A = 2000, RUNSET_B = 2200, TWIN_A = 2400, TWIN_B = 2500;
    const int JOKER = 52;

    public LayoutBox info;
    public GameObject hiliteH, hiliteV;          // opsiyonel: hedef hücre vurgusu (sahnede)
    readonly List<int> _nos = new();
    readonly System.Random _rng = new(5);
    readonly List<int> _free = new();

    protected override Vec2 ItemNaturalSize(bool compact) => compact ? new Vec2(44f, 55f) : new Vec2(100f, 130f);

    protected override void Start()
    {
        for (var i = 0; i < 104; i++) _free.Add(i);                   // 0..51 ×2 (taslak: No % 52 = yüz; 52 = joker)
        SetStack(ZONE_DECK, null, 48, animated: false);
        _nos.Clear(); for (var i = 0; i < 21; i++) _nos.Add(Draw());
        SetGrid(ZoneOf(ZONE_MY_HAND), _nos, fromSlot: ZONE_DECK);
        SetStack(ZONE_INDICATOR, new List<int> { Draw() }, 1, fromSlot: ZONE_DECK);
        SetStack(ZONE_DISCARD0, new List<int> { Draw(), Draw() }, 2, fromSlot: ZONE_DECK);
        SetStack(ZONE_DISCARD1, new List<int> { Draw() }, 1, fromSlot: ZONE_DECK);
        // örnek perler: run 5-6-7 (kolon 4..6, satır 0), set 9 (kolon 8..10, satır 1, biri joker), çift (twin satır 0)
        SetRow(RUNSET_A + 0 * 13 + 4, new List<int> { 4, 5, 6 }, fromSlot: ZONE_DECK);
        SetRow(RUNSET_A + 1 * 13 + 8, new List<int> { 8, 21, JOKER }, fromSlot: ZONE_DECK);
        SetRow(TWIN_A + 0 * 2, new List<int> { 30, 30 + 52 }, fromSlot: ZONE_DECK);
        Say("deal");
    }

    int Draw() { var k = _rng.Next(_free.Count); var n = _free[k]; _free.RemoveAt(k); return n; }
    static int Rank(int no) => no == JOKER ? -1 : (no % 52) % 13;
    static int Suit(int no) => no == JOKER ? -1 : (no % 52) / 13;

    protected override bool CanDrag(Tile t)
    {
        if (t.CurrentPose.Slot >= RUNSET_A) return false;              // perden taş geri alınmaz
        if (t.CurrentPose.Slot == ZONE_DECK && t.No < 0) { t.No = Draw(); t.CurrentPose.Closed = false; t.Decorate(); }
        return true;
    }

    /// <summary>Hedefler: ıstaka (hole) + ıskartalar/gösterge (throw) + per hücreleri (kural örneği).</summary>
    protected override void OnDragBegin(IReadOnlyList<Tile> items, List<DropTarget> targets)
    {
        targets.Add(DropTarget.Zone(ZoneOf(ZONE_MY_HAND)));
        foreach (var z in Zones) if (z.layout == ZoneLayout.Stack && z.firstSlot != ZONE_DECK) targets.Add(DropTarget.Zone(z));

        var fromRack = ZoneOf(items[0].SavedPose.Slot)?.firstSlot == ZONE_MY_HAND;
        if (!fromRack) return;

        if (items.Count == 1)
        {
            // her per satırı: sol komşu, sağ komşu, joker hücresi (taslak: renk/sayı kontrolü yok)
            foreach (var z in Zones)
            {
                if (z.firstSlot < RUNSET_A) continue;
                for (var row = 0; row < z.rows; row++)
                {
                    int first = -1, last = -1;
                    for (var col = 0; col < z.columns; col++)
                    {
                        var s = z.firstSlot + row * z.columns + col;
                        var it = FindBySlot(s);
                        if (it == null) continue;
                        if (first < 0) first = col; last = col;
                        if (it.No == JOKER) targets.Add(DropTarget.Cell(s, tag: 1));          // okey alma → swap
                    }
                    if (first < 0) continue;
                    if (first > 0) targets.Add(DropTarget.Cell(z.firstSlot + row * z.columns + first - 1));
                    if (last < z.columns - 1) targets.Add(DropTarget.Cell(z.firstSlot + row * z.columns + last + 1));
                }
            }
        }
        else
        {
            // grup: ilk boş runset satırında rank kolonundan başlayarak
            var col = System.Math.Clamp(Rank(items[0].No), 0, 13 - items.Count);
            foreach (var zf in new[] { RUNSET_A, RUNSET_B })
            {
                var z = ZoneOf(zf);
                for (var row = 0; row < z.rows; row++)
                {
                    var empty = true;
                    for (var c = 0; c < z.columns && empty; c++) if (FindBySlot(z.firstSlot + row * z.columns + c) != null) empty = false;
                    if (!empty) continue;
                    targets.Add(DropTarget.Cell(z.firstSlot + row * z.columns + col, tag: 2));
                    return;
                }
            }
        }
    }

    protected override void OnDragHover(DropTarget t)
    {
        var z = t.slot >= 0 ? ZoneOf(t.slot) : null;
        var overMeld = z != null && z.firstSlot >= RUNSET_A;
        SetDragCompact(overMeld, overMeld ? z.itemSize : ZoneOf(ZONE_MY_HAND).itemSize);
        if (hiliteH != null) hiliteH.SetActive(overMeld && !t.wholeZone);
        if (overMeld && !t.wholeZone && hiliteH != null)
        {
            PoseOfSlot(t.slot, out var pos, out _, out _, out var parent);
            hiliteH.transform.SetParent(parent.transform, false);
            hiliteH.transform.localPosition = pos;
        }
        Say(t.slot < 0 ? "" : t.wholeZone ? $"over zone {t.slot}" : $"over cell {t.slot} tag {t.tag}");
    }

    protected override PlaceResult OnPlace(IReadOnlyList<Tile> items, DropTarget t)
    {
        Say($"place {items.Count} → {t.slot} tag {t.tag}");
        if (t.tag == 1)
        {
            // okey alma: joker sürüklenen taşın geldiği ıstaka hücresine döner
            return PlaceResult.Accept(swapTo: FindFreeSlot(ZoneOf(ZONE_MY_HAND), items[0].SavedPose.Slot));
        }
        return PlaceResult.Accept();
    }

    protected override bool OnThrow(Tile t, int target) { Say($"throw {t.No} → {target}"); return true; }
    protected override bool OnPick(Tile t, int source) { Say($"pick {t.No} from {source}"); return true; }
    protected override void OnItemTapped(Tile t) => Say($"tap {t.No} @ {t.CurrentPose.Slot}");

    void Say(string s) { if (info != null) info.Text = s; }
}
