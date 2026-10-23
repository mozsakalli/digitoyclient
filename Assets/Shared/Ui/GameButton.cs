// Oyun butonu (prod GameButton): PASS / KNOCK / GIN sprite'ları; Knock/Gin'de nefes alan parlama (alpha 0.1↔1, 0.75s InOutSine, döngü),
// Knock'ta skor yazısı. Disabled = gizli. Tık → Clicked. Basma geri bildirimi ölçek (Button gibi).
using System;
using DigitoyEngine;

namespace Digitoy.Ui
{
    public enum GameButtonState { Disabled, Pass, Knock, Gin }

    public class GameButton : Component
    {
        public SpriteRenderer face;
        public SpriteRenderer glow;
        public LayoutBox scoreLabel;
        public Sprite passSprite, knockSprite, ginSprite;
        public Color knockGlow = new Color { r = 255, g = 220, b = 60, a = 255 };
        public Color ginGlow = new Color { r = 80, g = 230, b = 255, a = 255 };
        public float pressScale = 0.94f;

        public event Action<GameButton> Clicked;
        public GameButtonState State { get; private set; } = GameButtonState.Disabled;

        TweenHandle _breath, _scale;
        Vec3 _baseScale;
        bool _breathUp;

        protected override void Awake()
        {
            _baseScale = transform.localScale;
            Apply();
        }

        public void SetState(GameButtonState s, int knockScore = 0)
        {
            if (State == s && s != GameButtonState.Knock) return;
            State = s;
            if (scoreLabel != null) scoreLabel.Text = s == GameButtonState.Knock && knockScore > 0 ? knockScore.ToString() : "";
            Apply();
        }

        void Apply()
        {
            var on = State != GameButtonState.Disabled;
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (!on) { _breath.Cancel(); return; }
            if (face != null) face.Sprite = State switch { GameButtonState.Knock => knockSprite, GameButtonState.Gin => ginSprite, _ => passSprite };
            if (glow != null)
            {
                var breathe = State != GameButtonState.Pass;
                glow.gameObject.SetActive(breathe);
                if (breathe)
                {
                    var c = State == GameButtonState.Gin ? ginGlow : knockGlow; c.a = 25; glow.Color = c;
                    _breathUp = true; Breathe();
                }
                else _breath.Cancel();
            }
        }

        void Breathe()
        {
            _breath.Cancel();
            if (glow == null || !glow.gameObject.activeSelf) return;
            _breath = glow.TweenAlpha(_breathUp ? 1f : 0.1f, 0.75f).Ease(Ease.InOutSine).OnDone(() => { _breathUp = !_breathUp; Breathe(); });
        }

        protected override void OnPointerDown(PointerEvent e)
        {
            _scale.Cancel();
            _scale = transform.TweenScale(new Vec2(_baseScale.x * pressScale, _baseScale.y * pressScale), 0.08f).Ease(Ease.OutCubic);
        }

        protected override void OnPointerUp(PointerEvent e)
        {
            _scale.Cancel();
            _scale = transform.TweenScale(new Vec2(_baseScale.x, _baseScale.y), 0.25f).Ease(Ease.OutBack);
        }

        protected override void OnPointerClick(PointerEvent e)
        {
            if (e.dragged || State == GameButtonState.Disabled) return;
            Clicked?.Invoke(this);
            e.Use();
        }

        protected override void OnDisable()
        {
            _breath.Cancel(); _scale.Cancel();
            transform.localScale = _baseScale;
        }
    }
}
