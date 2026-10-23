// Koltuk (prod TableSeat): avatar dairesi + nick/skor paneli + sıra parlaması + süre halkası + konuşma balonu (Ready/Pass/Rematch).
// Oyun bilmez: Bind(nick, score, balance) / SetTurn(on, remaining, total) / ShowBubble(kind). Görseller sahnede/prefab'ta (tema), burada yalnız davranış.
// Zamanlı görsel değişim Tween ile (tek sahip: bu component). Boş koltuk: avatar/panel gizli, "waiting" görünümü.
using DigitoyEngine;

namespace Digitoy.Ui
{
    public enum SeatBubbleKind { None, Ready, Pass, Rematch, Leaving }

    public class Seat : Component
    {
        public GameObject avatarRoot;          // avatar daire (frame + bg + resim)
        public GameObject infoRoot;            // nick + skor paneli
        public LayoutBox nick, score, balance;
        public SpriteRenderer glow;            // Seat_Glow: sıra bende parlar
        public ProgressRenderer timer;         // SeatTimer: kalan süre halkası
        public SpriteRenderer timerThumb;      // halka ucundaki nokta (opsiyonel)
        public GameObject bubbleRoot;          // Seat_Speech_Bubble kökü
        public SpriteRenderer bubbleText;      // içindeki metin sprite'ı
        public Sprite bubbleReady, bubblePass, bubbleRematch, bubbleLeaving;
        public bool mirrorBubble;              // sağ koltuk: balon sola açılır

        public float enterDuration = 0.25f;
        public float bubbleHold = 1.5f;

        bool _occupied, _turn;
        float _turnTotal = 20f, _turnRemaining;
        TweenHandle _avatarScale, _glowAlpha, _bubbleScale, _bubbleAlpha, _bubbleHoldT;
        Vec3 _avatarBase, _bubbleBase;

        protected override void Awake()
        {
            if (avatarRoot != null) _avatarBase = avatarRoot.transform.localScale;
            if (bubbleRoot != null) { _bubbleBase = bubbleRoot.transform.localScale; bubbleRoot.SetActive(false); }
            if (glow != null) { var c = glow.Color; c.a = 0; glow.Color = c; }
            if (timer != null) timer.gameObject.SetActive(false);
        }

        /// <summary>Koltuğu doldur/boşalt. İlk doluşta giriş animasyonu (avatar 0→1 OutBack, panel kayarak).</summary>
        public void Bind(bool occupied, string nickText, string scoreText, string balanceText)
        {
            if (nick != null) nick.Text = nickText ?? "";
            if (score != null) score.Text = scoreText ?? "";
            if (balance != null) balance.Text = balanceText ?? "";
            if (occupied == _occupied) return;
            _occupied = occupied;
            if (avatarRoot != null) avatarRoot.SetActive(occupied);
            if (infoRoot != null) infoRoot.SetActive(occupied);
            if (!occupied) { SetTurn(false, 0, 0); return; }
            if (avatarRoot != null)
            {
                _avatarScale.Cancel();
                avatarRoot.transform.localScale = new Vec3(0f, 0f, 1f);
                _avatarScale = avatarRoot.transform.TweenScale(new Vec2(_avatarBase.x, _avatarBase.y), enterDuration).Ease(Ease.OutBack);
            }
        }

        /// <summary>Sıra bu koltukta mı. remaining/total saniye; halka remaining/total ile dolar, Update'te akar.</summary>
        public void SetTurn(bool on, float remaining, float total)
        {
            _turn = on; _turnTotal = total > 0 ? total : 20f; _turnRemaining = remaining;
            _glowAlpha.Cancel();
            if (glow != null) _glowAlpha = glow.TweenAlpha(on ? 1f : 0f, 0.25f).Ease(Ease.OutCubic);
            if (timer != null)
            {
                if (on && remaining > 0) { timer.gameObject.SetActive(true); timer.Value = System.Math.Clamp(remaining / _turnTotal, 0f, 1f); }
                else timer.gameObject.SetActive(false);
            }
        }

        protected override void Update()
        {
            if (!_turn || timer == null || !timer.gameObject.activeSelf) return;
            _turnRemaining -= Time.deltaTime;
            if (_turnRemaining <= 0f) { _turnRemaining = 0f; timer.Value = 0f; return; }
            timer.Value = _turnRemaining / _turnTotal;
            if (timerThumb != null)
            {
                // halkanın ucunu takip et (yarıçap = Width/2 - Thickness/2)
                var ang = (timer.StartAngle + (timer.Clockwise ? -1f : 1f) * 360f * timer.Value) * (System.MathF.PI / 180f);
                var r = timer.Width * 0.5f - timer.Thickness * 0.5f;
                timerThumb.transform.localPosition = new Vec3(System.MathF.Cos(ang) * r, -System.MathF.Sin(ang) * r, timerThumb.transform.localPosition.z);
            }
        }

        /// <summary>Balon: 0→1 OutBack 0.5s + alpha 0.3s, bekle, 0.8 InBack 0.3s + fade.</summary>
        public void ShowBubble(SeatBubbleKind kind)
        {
            if (bubbleRoot == null) return;
            var s = kind switch { SeatBubbleKind.Ready => bubbleReady, SeatBubbleKind.Pass => bubblePass, SeatBubbleKind.Rematch => bubbleRematch, SeatBubbleKind.Leaving => bubbleLeaving, _ => null };
            if (s == null) { bubbleRoot.SetActive(false); return; }
            if (bubbleText != null) bubbleText.Sprite = s;
            _bubbleScale.Cancel(); _bubbleAlpha.Cancel(); _bubbleHoldT.Cancel();
            bubbleRoot.SetActive(true);
            var t = bubbleRoot.transform;
            t.localScale = new Vec3(0f, 0f, 1f);
            var sx = mirrorBubble ? -_bubbleBase.x : _bubbleBase.x;
            _bubbleScale = t.TweenScale(new Vec2(sx, _bubbleBase.y), 0.5f).Ease(Ease.OutBack);
            _bubbleHoldT = gameObject.scene.TweenValue(0f, 1f, 0.5f + bubbleHold).OnDone(() =>
            {
                _bubbleScale = t.TweenScale(new Vec2(sx * 0.8f, _bubbleBase.y * 0.8f), 0.3f).Ease(Ease.InBack).OnDone(() => bubbleRoot.SetActive(false));
            });
        }

        protected override void OnDisable()
        {
            _avatarScale.Cancel(); _glowAlpha.Cancel(); _bubbleScale.Cancel(); _bubbleAlpha.Cancel(); _bubbleHoldT.Cancel();
            if (avatarRoot != null) avatarRoot.transform.localScale = _avatarBase;
            if (bubbleRoot != null) { bubbleRoot.transform.localScale = _bubbleBase; bubbleRoot.SetActive(false); }
        }
    }
}
