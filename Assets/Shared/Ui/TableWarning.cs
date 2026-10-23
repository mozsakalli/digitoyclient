// Masa uyarısı (prod TableWarning): panel (0,220)'de; 30px aşağıdan kayarak belirir (0.18s OutCubic), 1.25s kalır, kayarak gider (0.2s InCubic). (LayoutBox alpha tween yolu yok → fade yok.)
// Aynı metin 0.4s içinde tekrar gelirse yutulur; yeni uyarı öncekini keser. Metin anahtarı loc tablosundan (table.warning.*).
using DigitoyEngine;

namespace Digitoy.Ui
{
    public class TableWarning : Component
    {
        public LayoutBox label;
        public float hold = 1.25f;
        public Vec2 slide = new Vec2(0f, 30f);

        string _last; float _lastAt = -10f;
        Vec3 _home;
        TweenHandle _move, _holdT;

        protected override void Awake()
        {
            _home = transform.localPosition;
            gameObject.SetActive(false);
        }

        public void Show(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var now = Time.time;
            if (text == _last && now - _lastAt < 0.4f) return;
            _last = text; _lastAt = now;
            _move.Cancel(); _holdT.Cancel();
            if (label != null) label.Text = text;
            gameObject.SetActive(true);
            transform.localPosition = new Vec3(_home.x + slide.x, _home.y + slide.y, _home.z);
            _move = transform.TweenMove(_home, 0.18f).Ease(Ease.OutCubic);
            _holdT = gameObject.scene.TweenValue(0f, 1f, 0.18f + hold).OnDone(Hide);
        }

        void Hide()
        {
            _move = transform.TweenMove(new Vec3(_home.x + slide.x, _home.y + slide.y, _home.z), 0.2f).Ease(Ease.InCubic).OnDone(() => gameObject.SetActive(false));
        }

        protected override void OnDisable()
        {
            _move.Cancel(); _holdT.Cancel();
            transform.localPosition = _home;
        }
    }
}
