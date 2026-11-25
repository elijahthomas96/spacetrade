using UnityEngine;

namespace SpaceTrade.Game
{
    /// <summary>Procedural textures and sprites, so the project needs no imported art.</summary>
    public static class SpriteFactory
    {
        private static Sprite _disc, _ring, _shipIcon, _shipDetail, _pixel, _glow, _streak;

        public static Sprite Disc { get { return _disc ?? (_disc = Make(Circle(64, 0.0f, 1.0f, 0.12f), 64)); } }
        public static Sprite Ring { get { return _ring ?? (_ring = Make(Circle(96, 0.80f, 0.95f, 0.04f), 96)); } }
        public static Sprite Glow { get { return _glow ?? (_glow = Make(RadialGlow(64), 64)); } }
        public static Sprite Pixel { get { return _pixel ?? (_pixel = Make(Solid(4, Color.white), 4)); } }
        public static Sprite ShipIcon { get { return _shipIcon ?? (_shipIcon = Make(Arrow(48), 48)); } }
        public static Sprite ShipDetail { get { return _shipDetail ?? (_shipDetail = Make(DetailedShip(256), 256)); } }
        public static Sprite Streak { get { return _streak ?? (_streak = Make(StreakTex(64, 8), 64)); } }

        public static Texture2D SolidTexture(Color c) { return Solid(2, c); }

        private static Sprite Make(Texture2D tex, float pixelsPerUnit)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
        }

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.DontSave;
            return t;
        }

        private static Texture2D Solid(int size, Color c)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D Circle(int size, float inner, float outer, float soft)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Clamp01((outer - d) / soft) * (inner <= 0 ? 1 : Mathf.Clamp01((d - inner) / soft));
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D RadialGlow(int size)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                    float a = Mathf.Pow(Mathf.Clamp01(1 - d), 2.2f);
                    px[y * size + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Arrow-shaped map icon pointing along +X.</summary>
        private static Texture2D Arrow(int size)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            Vector2 a = new Vector2(size * 0.95f, size * 0.5f), b = new Vector2(size * 0.08f, size * 0.12f), c = new Vector2(size * 0.08f, size * 0.88f);
            Vector2 notch = new Vector2(size * 0.3f, size * 0.5f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    bool inside = (InTri(p, a, b, notch) || InTri(p, a, notch, c));
                    px[y * size + x] = new Color(1, 1, 1, inside ? 1 : 0);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Close-up hull: long spine, warp ring, nacelles and engine block, pointing along +X, white so it can be tinted.</summary>
        private static Texture2D DetailedShip(int size)
        {
            var t = NewTex(size, size);
            var px = new Color[size * size];
            float s = size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                    float a = 0, shade = 1;
                    // Spine
                    if (u > 0.12f && u < 0.86f && Mathf.Abs(v - 0.5f) < 0.045f + 0.03f * Mathf.Clamp01((0.86f - u) * 2)) { a = 1; shade = 0.95f; }
                    // Bow
                    if (u >= 0.86f && u < 0.97f && Mathf.Abs(v - 0.5f) < 0.06f * (0.97f - u) / 0.11f) { a = 1; shade = 1f; }
                    // Cargo modules
                    if (u > 0.36f && u < 0.66f && Mathf.Abs(v - 0.5f) < 0.11f && ((int)(u * 40) % 3 != 0)) { a = 1; shade = 0.75f; }
                    // Warp ring (seen edge-on as an ellipse)
                    float ex = (u - 0.28f) / 0.05f, ey = (v - 0.5f) / 0.36f;
                    float ell = ex * ex + ey * ey;
                    if (ell < 1f && ell > 0.55f) { a = 1; shade = 0.85f; }
                    // Nacelles
                    if (u > 0.08f && u < 0.34f && (Mathf.Abs(v - 0.27f) < 0.035f || Mathf.Abs(v - 0.73f) < 0.035f)) { a = 1; shade = 0.8f; }
                    // Engine block
                    if (u > 0.05f && u < 0.14f && Mathf.Abs(v - 0.5f) < 0.09f) { a = 1; shade = 0.6f; }
                    // Bridge windows
                    if (u > 0.78f && u < 0.84f && Mathf.Abs(v - 0.5f) < 0.015f) { a = 1; shade = 0.3f; }
                    px[y * size + x] = new Color(shade, shade, shade, a);
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Texture2D StreakTex(int w, int h)
        {
            var t = NewTex(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1), v = Mathf.Abs(y - (h - 1) * 0.5f) / (h * 0.5f);
                    px[y * w + x] = new Color(1, 1, 1, u * Mathf.Clamp01(1 - v));
                }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) { return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y); }
    }
}
