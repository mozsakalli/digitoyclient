// Metin + MovieClip'li bildirim parçası (hata toast'ı vb.). Yerleşim/koreografi tamamen prefab'da:
// MovieClip kare 0 = gizli duruş, Show() metni yazar ve klibi baştan oynatır (giriş → bekleme → çıkış).
using DigitoyEngine;

namespace Digitoy.Ui
{
    public sealed class ClipToast : Component
    {
        public LayoutBox text;
        public MovieClip clip;

        public void Show(string message)
        {
            if (text != null) text.Text = message;
            clip?.GotoAndPlay(0);
        }

        public void Hide()
        {
            clip?.GotoAndStop(0);
        }
    }
}
