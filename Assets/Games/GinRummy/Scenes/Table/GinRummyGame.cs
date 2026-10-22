// Gin masası: slot sabitleri + kurallar. Şimdilik test: Start'ta sahte dağıtım (deste 31, el 10, ıskarta 1); her hamle serbest.
// Sunucu bağı (GinTableService) sonraki adım.
using System.Collections.Generic;
using Digitoy;
using DigitoyEngine;

public class GinRummyGame : BoardGameManager<Card>
{
    public LayoutBox info;
    readonly List<int> _nos = new();
    readonly System.Random _rng = new(1);
    readonly List<int> _free = new();

    protected override Vec2 ItemNaturalSize(bool compact) => new Vec2(194f, 280f);

    protected override void Start()
    {
        for (var i = 0; i < 52; i++) _free.Add(i);
        SetStack(ZONE_DECK, null, 31, animated: false);
        _nos.Clear(); for (var i = 0; i < 10; i++) _nos.Add(Draw());
        SetRow(ZONE_MY_HAND, _nos, fromSlot: ZONE_DECK);
        SetStack(ZONE_DISCARD0, new List<int> { Draw() }, 1, fromSlot: ZONE_DECK);
        Say("deal");
    }

    int Draw() { var k = _rng.Next(_free.Count); var n = _free[k]; _free.RemoveAt(k); return n; }

    protected override bool CanDrag(Card c)
    {
        // desteden çekiş: kaldırınca kimlik alır
        if (c.CurrentPose.Slot == ZONE_DECK && c.No < 0) { c.No = Draw(); c.CurrentPose.Closed = false; c.Decorate(); }
        return true;
    }

    protected override bool OnThrow(Card c, int target)
    {
        Say($"throw {c.No} → {target}");
        return target == ZONE_DISCARD0;          // yalnız ıskartaya atılır (desteye değil)
    }

    protected override bool OnPick(Card c, int source) { Say($"pick {c.No} from {source}"); return true; }
    protected override void OnItemTapped(Card c) => Say($"tap {c.No} @ {c.CurrentPose.Slot}");

    void Say(string s) { if (info != null) info.Text = s; }
}
