// 101 taşı: No 0..105 (sunucu kimliği; renk/sayı türetimi sonra Common'dan), görsel tile_N / tile_small_N (N = No % 54 — taslak eşleme).
// Kompakt bölgede (per/ıskarta) küçük sprite, ıstakada tam sprite; kapalı = arka (tile_52/53 sahte okey yüzü yerine şimdilik tile_0 koyu).
using System.Collections.Generic;
using Digitoy;
using DigitoyEngine;

public class Tile : BoardGameItem<Tile>
{
    public List<Sprite> Sprites;
    public List<Sprite> SmallSprites;
    public Sprite Back;
    public Sprite SmallBack;
    public GameObject Face;
    public GameObject Highlight;

    SpriteRenderer _face;

    public override void Decorate()
    {
        _face ??= Face?.GetComponent<SpriteRenderer>();
        if (_face == null) return;
        var closed = CurrentPose.Closed || No < 0;
        var compact = CurrentPose.Compact;
        var list = compact ? SmallSprites : Sprites;
        // taslak eşleme: 0..51 ve 52..103 iki kopya (No % 52), 52 = joker; görsel index 52 = joker sprite'ı
        var idx = No == 52 ? 52 : (No % 52 + 52) % 52;
        idx = System.Math.Min(idx, list.Count - 1);
        _face.Sprite = closed ? (compact ? SmallBack : Back) : list[idx];
        // sprite boyu bölge ölçeğiyle değil doğal boyla: compact sprite küçük, ölçek 1 → manager ölçeği doğal boya göre hesaplar
        if (compact) { _face.Width = 44f; _face.Height = 55f; } else { _face.Width = 100f; _face.Height = 130f; }
        if (Highlight != null && Highlight.activeSelf != Dragging) Highlight.SetActive(Dragging);
    }

    public override void OnMultiSelect() => Decorate();
}
