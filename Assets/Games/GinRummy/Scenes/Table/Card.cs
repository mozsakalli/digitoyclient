using DigitoyEngine;
using Digitoy;
using System.Collections.Generic;

public class Card : BoardGameItem<Card>
{
    public List<Sprite> Sprites;
    public Sprite Back;
    public GameObject Highlight;
    public GameObject Face;
    public GameObject Shadow;

    SpriteRenderer _face;

    public override void Decorate()
    {
        _face ??= Face?.GetComponent<SpriteRenderer>();
        if (_face == null) return;
        var closed = CurrentPose.Closed || No < 0;
        _face.Sprite = closed ? Back : Sprites[System.Math.Clamp(No, 0, Sprites.Count - 1)];
    }
}