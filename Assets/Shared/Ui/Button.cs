// Genel buton davranışı: basınca küçül, bırakınca OutBack ile geri gel, tıklamada Clicked. Görünüm LayoutBox'tadır;
// yalnız transform ölçeğine dokunur (Tween havuzu: sıfır alloc, destroy-güvenli). Aynı GO'da ölçek yazan başka component olmasın.
using System;
using DigitoyEngine;

namespace Digitoy.Ui
{
    public class Button : Component
    {
        public string buttonId = "";
        public float pressScale = 0.94f;
        public float pressDuration = 0.08f;
        public float releaseDuration = 0.25f;
        public bool interactable = true;

        public event Action<Button, PointerEvent> Clicked;
        public static event Action<Button, PointerEvent> AnyClicked;

        private Vec3 _baseScale;
        private bool _captured;
        private bool _pressed;
        private TweenHandle _scale;

        public bool IsPressed => _pressed;

        protected override void Awake() => Capture();

        private void Capture()
        {
            if (_captured) return;
            _captured = true;
            _baseScale = transform.localScale;
        }

        private void ScaleTo(float k, float dur, Ease ease)
        {
            _scale.Cancel();
            var to = new Vec2(_baseScale.x * k, _baseScale.y * k);
            if (dur <= 0f)
            {
                transform.localScale = new Vec3(to.x, to.y, _baseScale.z);
                return;
            }
            _scale = transform.TweenScale(to, dur).Ease(ease);
        }

        protected override void OnPointerDown(PointerEvent e)
        {
            if (!interactable) { e.Use(); return; }
            Capture();
            _pressed = true;
            ScaleTo(pressScale, pressDuration, Ease.OutCubic);
        }

        protected override void OnPointerUp(PointerEvent e)
        {
            if (!_pressed) return;
            _pressed = false;
            ScaleTo(1f, releaseDuration, Ease.OutBack);
        }

        protected override void OnDragStart(PointerEvent e)
        {
            if (!_pressed) return;
            _pressed = false;
            ScaleTo(1f, releaseDuration, Ease.OutCubic);
        }

        protected override void OnPointerClick(PointerEvent e)
        {
            if (!interactable || e.dragged) return;
            Clicked?.Invoke(this, e);
            AnyClicked?.Invoke(this, e);
            e.Use();
        }

        protected override void OnDisable()
        {
            _pressed = false;
            _scale.Cancel();
            if (_captured) transform.localScale = _baseScale;
        }
    }
}
