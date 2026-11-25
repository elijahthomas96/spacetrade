using UnityEngine;

namespace SpaceTrade.Game
{
    /// <summary>Runtime-built IMGUI skin. IMGUI reads input through its own event stream, so it works with either input backend.</summary>
    public static class UiKit
    {
        public static readonly Color Bg = new Color(0.06f, 0.08f, 0.13f, 0.94f);
        public static readonly Color BgLight = new Color(0.11f, 0.14f, 0.21f, 0.97f);
        public static readonly Color Accent = new Color(0.35f, 0.75f, 1f);
        public static readonly Color GoodC = new Color(0.45f, 0.92f, 0.55f);
        public static readonly Color BadC = new Color(1f, 0.42f, 0.42f);
        public static readonly Color WarnC = new Color(1f, 0.78f, 0.3f);
        public static readonly Color MutedC = new Color(0.62f, 0.68f, 0.78f);
        public static readonly Color TextC = new Color(0.9f, 0.93f, 0.97f);

        public static GUIStyle Panel, Card, Title, H2, Label, Small, Muted, Good, Bad, Warn, Btn, BtnOn, BtnSmall, Row, RowOn, Center, Mono, Toggle, Field;

        private static bool _ready;
        private static Texture2D _white;

        public static Texture2D White { get { if (_white == null) _white = SpriteFactory.SolidTexture(Color.white); return _white; } }

        public static void Init()
        {
            if (_ready && Panel != null && Panel.normal.background != null) return;
            _ready = true;
            var font = GUI.skin.label.font;

            Panel = Box(Bg, 10);
            Card = Box(BgLight, 8);
            Label = Text(13, TextC);
            Label.wordWrap = true;
            Small = Text(11, TextC);
            Small.wordWrap = true;
            Muted = Text(11, MutedC);
            Muted.wordWrap = true;
            Good = Text(13, GoodC); Good.wordWrap = true;
            Bad = Text(12, BadC); Bad.wordWrap = true;
            Warn = Text(12, WarnC); Warn.wordWrap = true;
            Title = Text(18, Color.white); Title.fontStyle = FontStyle.Bold;
            H2 = Text(13, Accent); H2.fontStyle = FontStyle.Bold; H2.margin = new RectOffset(0, 0, 8, 2);
            Center = Text(13, TextC); Center.alignment = TextAnchor.MiddleCenter;
            Mono = Text(12, TextC);
            Mono.font = font;

            Btn = Button(new Color(0.17f, 0.24f, 0.36f), new Color(0.24f, 0.34f, 0.5f), 13);
            BtnOn = Button(new Color(0.2f, 0.5f, 0.75f), new Color(0.28f, 0.6f, 0.86f), 13);
            BtnSmall = Button(new Color(0.17f, 0.24f, 0.36f), new Color(0.24f, 0.34f, 0.5f), 11);
            BtnSmall.padding = new RectOffset(6, 6, 2, 2);
            Row = Button(new Color(0.09f, 0.12f, 0.19f, 0.9f), new Color(0.15f, 0.2f, 0.3f, 0.95f), 12);
            Row.alignment = TextAnchor.MiddleLeft;
            Row.wordWrap = true;
            RowOn = Button(new Color(0.16f, 0.3f, 0.46f), new Color(0.2f, 0.36f, 0.54f), 12);
            RowOn.alignment = TextAnchor.MiddleLeft;
            RowOn.wordWrap = true;

            Toggle = new GUIStyle(GUI.skin.toggle) { fontSize = 12 };
            Toggle.normal.textColor = Toggle.onNormal.textColor = Toggle.hover.textColor = Toggle.onHover.textColor = TextC;
            Field = new GUIStyle(GUI.skin.textField) { fontSize = 13 };
        }

        private static GUIStyle Text(int size, Color c)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, richText = true };
            s.normal.textColor = c;
            s.margin = new RectOffset(0, 0, 1, 1);
            s.padding = new RectOffset(0, 0, 0, 0);
            return s;
        }

        private static GUIStyle Box(Color c, int pad)
        {
            var s = new GUIStyle(GUI.skin.box);
            s.normal.background = SpriteFactory.SolidTexture(c);
            s.padding = new RectOffset(pad, pad, pad, pad);
            s.margin = new RectOffset(0, 0, 0, 6);
            s.border = new RectOffset(0, 0, 0, 0);
            return s;
        }

        private static GUIStyle Button(Color normal, Color hover, int size)
        {
            var s = new GUIStyle(GUI.skin.button) { fontSize = size, richText = true };
            s.normal.background = SpriteFactory.SolidTexture(normal);
            s.hover.background = SpriteFactory.SolidTexture(hover);
            s.active.background = SpriteFactory.SolidTexture(hover * 1.15f);
            s.focused.background = s.normal.background;
            s.onNormal.background = s.normal.background;
            s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = TextC;
            s.border = new RectOffset(0, 0, 0, 0);
            s.padding = new RectOffset(8, 8, 4, 4);
            s.margin = new RectOffset(2, 2, 2, 2);
            return s;
        }

        public static string Hex(Color c) { return ColorUtility.ToHtmlStringRGB(c); }

        public static string Colored(string text, Color c) { return "<color=#" + Hex(c) + ">" + text + "</color>"; }

        public static void Bar(Rect r, float frac, Color fill)
        {
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.12f);
            GUI.DrawTexture(r, White);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(frac), r.height), White);
            GUI.color = old;
        }

        public static void Swatch(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }
    }
}
