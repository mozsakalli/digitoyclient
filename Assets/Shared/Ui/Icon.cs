// Shared/Fonts/Digitoy-Icon.ttf.meta customGlyphs ile birebir (ASCII harf; a–z, A–J). Metinde: Icon.Tag(Icon.Facebook) + "  CONNECT"
// → "<font=icon>a</font>  CONNECT". Loc tablosunda da aynı rich text yazılır.
namespace Digitoy.Ui
{
    public static class Icon
    {
        public const string Facebook = "a";
        public const string Apple = "b";
        public const string AddFriend = "c";
        public const string Buble = "d";
        public const string Calendar = "e";
        public const string Chat = "f";
        public const string ChevronLeft = "g";
        public const string ChevronRight = "h";
        public const string Chip = "i";
        public const string Clock = "j";
        public const string Close = "k";
        public const string Cog = "l";
        public const string Coin = "m";
        public const string Copy = "n";
        public const string Crown = "o";
        public const string Device = "p";
        public const string Edit = "q";
        public const string Edit1 = "r";
        public const string Email = "s";
        public const string Exit = "t";
        public const string Friends = "u";
        public const string Hand = "v";
        public const string Lock = "w";
        public const string Menu = "x";
        public const string Message = "y";
        public const string SearchFriend = "z";
        public const string Smiley = "A";
        public const string Star = "B";
        public const string Support = "C";
        public const string Target = "D";
        public const string Tick = "E";
        public const string Tomato = "F";
        public const string Trash = "G";
        public const string Trophy = "H";
        public const string Video = "I";
        public const string Warning = "J";

        public const string FontName = "icon";

        /// <summary>İkon harfini rich text'te ikon fontuna sarar: "<font=icon>a</font>".</summary>
        public static string Tag(string icon) => "<font=" + FontName + ">" + icon + "</font>";
    }
}
