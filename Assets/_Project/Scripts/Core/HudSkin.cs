using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>
    /// Оформление HUD: шрифты, процедурные текстуры (пергамент, бронза, дерево, печать),
    /// геометрические иконки и стили IMGUI. Раскладка задаётся в «виртуальных» пикселях экрана
    /// 1600×900; <see cref="R(Rect)"/> переводит их в экранные. Текстуры и шрифты пересобираются
    /// под текущий масштаб, поэтому рамки и текст остаются чёткими на любом разрешении.
    /// </summary>
    public static class HudSkin
    {
        // ---------- Палитра ----------
        public static readonly Color Teal = Hex("1f3b3d");
        public static readonly Color TealDark = Hex("132628");
        public static readonly Color BronzeLine = Hex("a67c3d");
        public static readonly Color Bronze = Hex("b8894a");
        public static readonly Color BronzeRim = Hex("6e4a1f");
        public static readonly Color ProgressColor = Hex("5fd0c2");
        public static readonly Color Wax = Hex("8a2b1e");
        public static readonly Color Card = Hex("f1e4c4");
        public static readonly Color Parchment = Hex("f6ecd4");
        public static readonly Color Wood = Hex("5a3b22");
        public static readonly Color GoldLine = Hex("c9a24a");
        public static readonly Color Ink = Hex("2b2118");
        public static readonly Color LightText = Hex("f1e4c4");
        public static readonly Color MutedLight = Hex("c9b891");

        /// <summary>Цвета для rich text на пергаменте (тёмные) и на бирюзе (светлые).</summary>
        public const string CGood = "#2e6b2e", CBad = "#a3321e", CWarn = "#94560f", CGold = "#7a4f00",
            CViolet = "#5b3f9a", CCyan = "#1f5f7a", CMuted = "#7a6a55",
            LGood = "#9fe3a0", LBad = "#ff9a80", LGold = "#f0c870", LMuted = "#c9b891";

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;

        // ---------- Масштаб ----------
        public static float S { get; private set; } = 1f;
        public static float W => Screen.width / S;
        public static float H => Screen.height / S;
        public static Rect R(Rect v) => new Rect(v.x * S, v.y * S, v.width * S, v.height * S);
        public static Rect R(float x, float y, float w, float h) => new Rect(x * S, y * S, w * S, h * S);
        public static int Sz(float virtualPx) => Mathf.Max(1, Mathf.RoundToInt(virtualPx * S));
        /// <summary>Мышь в виртуальных координатах (внутри OnGUI).</summary>
        public static Vector2 Mouse => Event.current.mousePosition / S;
        /// <summary>Мышь в виртуальных координатах (вне OnGUI).</summary>
        public static Vector2 InputMouse => new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y) / S;

        private static float _builtFor = -1f;

        /// <summary>Вызывать в начале OnGUI: считает масштаб и при его смене пересобирает скин.</summary>
        public static void Ensure()
        {
            float s = Mathf.Max(0.6f, Mathf.Min(Screen.height / 900f, Screen.width / 1280f));
            s = Mathf.Round(s * 20f) / 20f;
            if (Mathf.Approximately(s, _builtFor) && Sans != null) return;
            S = s;
            _builtFor = s;
            foreach (var t in _cache.Values) UnityEngine.Object.Destroy(t);
            _cache.Clear();
            LoadFonts();
            BuildStyles();
        }

        // ---------- Шрифты ----------
        public static Font Serif, SerifBold, Sans, SansBold, Narrow, NarrowBold;

        private static void LoadFonts()
        {
            if (Sans != null) return;
            Font F(string name) => Resources.Load<Font>("Fonts/" + name);
            Serif = F("PT_Serif-Web-Regular");
            SerifBold = F("PT_Serif-Web-Bold");
            Sans = F("PT_Sans-Web-Regular");
            SansBold = F("PT_Sans-Web-Bold");
            Narrow = F("PT_Sans-Narrow-Web-Regular");
            NarrowBold = F("PT_Sans-Narrow-Web-Bold");
        }

        // ---------- Стили ----------
        public static GUIStyle Title, Heading, HeadingLight, Num, NumInk, Caption, CaptionInk, Body, BodySmall, TextLight, TextLightSmall,
            Btn, BtnBig, Row, Node, NodeKnown, NodeCurrent, Plain, Center, CenterLight, TurnTitle, Monogram, Hint, InkMid, InkRight, BigTitle;

        private static GUIStyle Text(Font f, int size, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = true)
        {
            var st = new GUIStyle
            {
                font = f, fontSize = Sz(size), alignment = a, wordWrap = wrap, richText = true, clipping = TextClipping.Clip,
            };
            st.normal.textColor = c;
            return st;
        }

        private static GUIStyle ButtonStyle(Font f, int size, Color text, Texture2D n, Texture2D h, Texture2D a, int border, TextAnchor anchor, int padX)
        {
            var st = Text(f, size, text, anchor, true);
            st.normal.background = n;
            st.hover.background = h; st.hover.textColor = text;
            st.active.background = a; st.active.textColor = text;
            st.onNormal.background = a; st.onNormal.textColor = text;
            st.onHover.background = a; st.onHover.textColor = text;
            st.border = new RectOffset(border, border, border, border);
            st.padding = new RectOffset(Sz(padX), Sz(padX), Sz(3), Sz(3));
            return st;
        }

        private static void BuildStyles()
        {
            Title = Text(SerifBold, 26, LightText, TextAnchor.MiddleLeft, false);
            TurnTitle = Text(SerifBold, 22, Ink, TextAnchor.MiddleCenter, false);
            Heading = Text(SerifBold, 24, Ink, TextAnchor.MiddleLeft, false);
            HeadingLight = Text(SerifBold, 19, LightText, TextAnchor.MiddleLeft, false);
            Num = Text(SansBold, 20, LightText, TextAnchor.MiddleLeft, false);
            NumInk = Text(SansBold, 20, Ink, TextAnchor.MiddleLeft, false);
            Caption = Text(Narrow, 13, MutedLight, TextAnchor.MiddleLeft, false);
            CaptionInk = Text(Narrow, 13, Hex("5a4a36"), TextAnchor.UpperLeft, true);
            Body = Text(Sans, 14, Ink);
            BodySmall = Text(Narrow, 14, Ink);
            TextLight = Text(Sans, 14, LightText);
            TextLightSmall = Text(Narrow, 13, LightText);
            Monogram = Text(SerifBold, 58, LightText, TextAnchor.MiddleCenter, false);
            Hint = Text(Narrow, 14, LightText, TextAnchor.LowerCenter, true);
            InkMid = Text(Sans, 14, Ink, TextAnchor.MiddleLeft, false);
            InkRight = Text(Sans, 14, Ink, TextAnchor.MiddleRight, false);
            BigTitle = Text(SerifBold, 56, Ink, TextAnchor.MiddleCenter, false);
            Center = Text(Sans, 14, Ink, TextAnchor.MiddleCenter, true);
            CenterLight = Text(SansBold, 14, LightText, TextAnchor.MiddleCenter, false);
            Plain = new GUIStyle();

            int b = Sz(8);
            Btn = ButtonStyle(SansBold, 14, Hex("24170b"), Tex("btn", () => BronzeButton(0)), Tex("btnH", () => BronzeButton(1)),
                Tex("btnA", () => BronzeButton(2)), b, TextAnchor.MiddleCenter, 8);
            BtnBig = new GUIStyle(Btn) { fontSize = Sz(20), font = SerifBold };
            Row = ButtonStyle(Sans, 14, Ink, Tex("row", () => RowTex(Hex("efe0bd"), Hex("c9a24a"))), Tex("rowH", () => RowTex(Hex("f9efd6"), Hex("b8894a"))),
                Tex("rowA", () => RowTex(Hex("e2cc9c"), Hex("8a6a35"))), b, TextAnchor.MiddleLeft, 8);
            Node = ButtonStyle(Sans, 14, Ink, Tex("node", () => RowTex(Hex("efe0bd"), Hex("b8894a"))), Tex("nodeH", () => RowTex(Hex("fbf2dc"), Hex("8a6a35"))),
                Tex("nodeA", () => RowTex(Hex("e2cc9c"), Hex("6e4a1f"))), b, TextAnchor.MiddleLeft, 8);
            NodeKnown = ButtonStyle(Sans, 14, Ink, Tex("nodeK", () => RowTex(Hex("d9e6c0"), Hex("5f8a4a"))), Tex("nodeKH", () => RowTex(Hex("e3eecd"), Hex("4a7038"))),
                Tex("nodeKA", () => RowTex(Hex("c9d9aa"), Hex("4a7038"))), b, TextAnchor.MiddleLeft, 8);
            NodeCurrent = ButtonStyle(Sans, 14, Ink, Tex("nodeC", () => RowTex(Hex("f4dc96"), Hex("a67c3d"), 2f)), Tex("nodeCH", () => RowTex(Hex("f8e6ad"), Hex("8a5a1f"), 2f)),
                Tex("nodeCA", () => RowTex(Hex("e8cc7c"), Hex("8a5a1f"), 2f)), b, TextAnchor.MiddleLeft, 8);
        }

        // ---------- Кэш текстур ----------
        private static readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        public static Texture2D Tex(string key, Func<Texture2D> make)
        {
            if (_cache.TryGetValue(key, out var t) && t != null) return t;
            t = make();
            t.hideFlags = HideFlags.HideAndDontSave;
            _cache[key] = t;
            return t;
        }

        private static Texture2D White => Tex("white", () => Paint(1, 1, (x, y) => Color.white));

        /// <summary>Текстура из функции цвета; (x, y) — центр пикселя, y вниз.</summary>
        private static Texture2D Paint(int w, int h, Func<float, float, Color> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[(h - 1 - y) * w + x] = f(x + 0.5f, y + 0.5f);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        // ---------- SDF-примитивы ----------
        private static float Cov(float d) => Mathf.Clamp01(0.5f - d);

        private static float RoundBox(Vector2 p, Vector2 half, float r)
        {
            var q = new Vector2(Mathf.Abs(p.x) - half.x + r, Mathf.Abs(p.y) - half.y + r);
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - r;
        }

        private static float Circle(Vector2 p, Vector2 c, float r) => (p - c).magnitude - r;

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            var pa = p - a; var ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        private static float Polygon(Vector2 p, Vector2[] v)
        {
            float d = Vector2.Dot(p - v[0], p - v[0]);
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                var e = v[j] - v[i]; var w = p - v[i];
                var b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Vector2.Dot(e, e));
                d = Mathf.Min(d, Vector2.Dot(b, b));
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        private static float Ellipse(Vector2 p, Vector2 c, Vector2 r, float angle)
        {
            var q = p - c;
            float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
            q = new Vector2(cs * q.x + sn * q.y, -sn * q.x + cs * q.y);
            float k = Mathf.Min(r.x, r.y);
            return (new Vector2(q.x / r.x, q.y / r.y).magnitude - 1f) * k;
        }

        private static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            float H(int a, int b) { uint n = (uint)(a * 374761393 + b * 668265263); n = (n ^ (n >> 13)) * 1274126177u; return (n & 0xffff) / 65535f; }
            float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
            return Mathf.Lerp(Mathf.Lerp(H(xi, yi), H(xi + 1, yi), sx), Mathf.Lerp(H(xi, yi + 1), H(xi + 1, yi + 1), sx), sy);
        }

        private static Color Over(Color under, Color over, float a)
        {
            a *= over.a;
            float outA = a + under.a * (1f - a);
            if (outA <= 0f) return new Color(0, 0, 0, 0);
            var c = (over * a + under * under.a * (1f - a)) / outA;
            c.a = outA;
            return c;
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        // ---------- Панели (9-slice) ----------

        /// <summary>Скруглённый пергамент с тонкой каймой и внутренней золотой линией.</summary>
        private static Texture2D ParchmentBox(Color fill, float radius, Color edge, float edgeW, Color line, float lineInset, float alpha)
        {
            float k = S, r = radius * k;
            int border = Mathf.CeilToInt(r + (lineInset + 3f) * k);
            int size = border * 2 + 4;
            var half = new Vector2(size / 2f, size / 2f);
            return Paint(size, size, (x, y) =>
            {
                var p = new Vector2(x, y) - half;
                float d = RoundBox(p, half - Vector2.one * 0.5f, r);
                float n = Noise(x * 0.35f / k, y * 0.35f / k) * 0.6f + Noise(x * 1.3f / k, y * 1.3f / k) * 0.4f;
                var c = Shade(fill, 0.955f + 0.07f * n);
                c.a = alpha;
                c = Over(c, edge, Cov(-d - edgeW * k));
                if (line.a > 0f) c = Over(c, line, Cov(Mathf.Abs(d + lineInset * k) - 0.6f * k));
                c.a *= Cov(d);
                return c;
            });
        }

        /// <summary>Пергамент в резной деревянной раме с тонкой золотой линией.</summary>
        private static Texture2D WoodFrame()
        {
            float k = S, r = 16f * k, wood = 10f * k;
            int border = Mathf.CeilToInt(r + wood + 6f * k);
            int size = border * 2 + 4;
            var half = new Vector2(size / 2f, size / 2f);
            return Paint(size, size, (x, y) =>
            {
                var p = new Vector2(x, y) - half;
                float d = RoundBox(p, half - Vector2.one * 0.5f, r);
                var fill = Shade(Card, 0.96f + 0.06f * Noise(x * 0.3f / k, y * 0.3f / k));
                fill.a = 0.93f;
                float depth = -d; // расстояние внутрь от края
                float grain = Noise(x * 0.08f / k, y * 1.1f / k) * 0.6f + Noise(x * 0.5f / k, y * 0.5f / k) * 0.4f;
                var w = Shade(Wood, 0.75f + 0.5f * grain);
                // Резьба: светлый скос снаружи, тёмная канавка посередине рамы.
                float bevel = Mathf.Clamp01(1f - depth / (2.2f * k));
                w = Color.Lerp(w, Hex("8a6040"), bevel * 0.6f);
                w = Color.Lerp(w, Hex("2e1c0f"), Cov(Mathf.Abs(depth - wood * 0.55f) - 0.7f * k) * 0.7f);
                w = Color.Lerp(w, Hex("2e1c0f"), Mathf.Clamp01((depth - (wood - 2f * k)) / (2f * k)) * 0.6f);
                var c = depth < wood ? w : fill;
                if (depth >= wood - 1f && depth < wood) c = Color.Lerp(w, fill, depth - (wood - 1f));
                c = Over(c, GoldLine, Cov(Mathf.Abs(depth - (wood + 3f * k)) - 0.75f * k));
                c.a *= Cov(d);
                return c;
            });
        }

        /// <summary>Мягкая тень под окном.</summary>
        private static Texture2D ShadowTex()
        {
            float k = S, blur = 14f * k;
            int border = Mathf.CeilToInt(blur * 2 + 12f * k);
            int size = border * 2 + 4;
            var half = new Vector2(size / 2f, size / 2f);
            return Paint(size, size, (x, y) =>
            {
                float d = RoundBox(new Vector2(x, y) - half, half - Vector2.one * blur, 12f * k);
                float a = 1f - Mathf.SmoothStep(-blur * 0.5f, blur, d);
                return new Color(0.08f, 0.05f, 0.02f, 0.42f * a);
            });
        }

        private static Texture2D BronzeButton(int state)
        {
            float k = S, r = 6f * k;
            int size = Mathf.CeilToInt(8f * k) * 2 + 4;
            var half = new Vector2(size / 2f, size / 2f);
            var top = state == 1 ? Hex("e1b774") : state == 2 ? Hex("9c6f35") : Hex("d4a664");
            var bottom = state == 1 ? Hex("b98a48") : state == 2 ? Hex("7d5526") : Hex("a37439");
            return Paint(size, size, (x, y) =>
            {
                var p = new Vector2(x, y) - half;
                float d = RoundBox(p, half - Vector2.one * 0.5f, r);
                var c = Color.Lerp(top, bottom, y / size);
                c = Over(c, Hex("fff0c8"), Cov(Mathf.Abs(d + 2.2f * k) - 0.5f * k) * (y < size / 2f ? 0.45f : 0.1f));
                c = Over(c, BronzeRim, Cov(-d - 1.4f * k));
                c.a = Cov(d);
                return c;
            });
        }

        private static Texture2D RowTex(Color fill, Color edge, float edgeW = 1.2f)
        {
            float k = S, r = 6f * k;
            int size = Mathf.CeilToInt(8f * k) * 2 + 4;
            var half = new Vector2(size / 2f, size / 2f);
            return Paint(size, size, (x, y) =>
            {
                float d = RoundBox(new Vector2(x, y) - half, half - Vector2.one * 0.5f, r);
                var c = fill;
                c = Over(c, edge, Cov(-d - edgeW * k));
                c.a = Cov(d);
                return c;
            });
        }

        public static Texture2D BarTex => Tex("bar", () => Paint(1, 64, (x, y) =>
            Color.Lerp(Shade(Teal, 1.22f), Shade(Teal, 0.8f), y / 64f)));

        /// <summary>Мягкое золотое свечение (подсветка кнопки «Конец хода»).</summary>
        public static Texture2D Glow(float virtualSize)
        {
            int px = Sz(virtualSize);
            return Tex($"glow{px}", () => Paint(px, px, (x, y) =>
            {
                float t = new Vector2(x - px / 2f, y - px / 2f).magnitude / (px / 2f);
                var c = Hex("ffd77a");
                c.a = Mathf.Clamp01(1f - t) * Mathf.Clamp01(1f - t) * 0.9f;
                return c;
            }));
        }

        public static Texture2D WindowTex => Tex("window", () => ParchmentBox(Parchment, 12f, Hex("b08a50"), 1.2f, GoldLine, 5f, 1f));
        public static Texture2D CardTex => Tex("card", () => ParchmentBox(Card, 8f, GoldLine, 2.2f, Hex("a67c3d"), 5f, 1f));
        public static Texture2D TileTex => Tex("tile", () => ParchmentBox(Hex("e8d6a8"), 7f, Hex("b8894a"), 1.2f, new Color(0, 0, 0, 0), 0f, 0.95f));
        public static Texture2D PlaqueTex => Tex("plaque", () => ParchmentBox(Teal, 7f, BronzeLine, 1.5f, new Color(0.72f, 0.54f, 0.29f, 0.45f), 4f, 0.92f));
        public static Texture2D PanelTex => Tex("panel", WoodFrame);
        public static Texture2D Shadow => Tex("shadow", ShadowTex);

        private static int SliceOf(Texture2D t) => (t.width - 4) / 2;

        /// <summary>9-slice отрисовка текстуры в виртуальный прямоугольник.</summary>
        public static void Box(Rect v, Texture2D t)
        {
            if (Event.current.type != EventType.Repaint) return;
            int b = SliceOf(t);
            var st = new GUIStyle { border = new RectOffset(b, b, b, b) };
            st.normal.background = t;
            st.Draw(R(v), false, false, false, false);
        }

        public static void Window(Rect v)
        {
            Box(new Rect(v.x - 16f, v.y - 12f, v.width + 32f, v.height + 32f), Shadow);
            Box(v, WindowTex);
        }

        public static void Fill(Rect v, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(R(v), White);
            GUI.color = old;
        }

        public static void Draw(Rect v, Texture t, Color? tint = null)
        {
            if (Event.current.type != EventType.Repaint || t == null) return;
            var old = GUI.color;
            GUI.color = tint ?? Color.white;
            GUI.DrawTexture(R(v), t);
            GUI.color = old;
        }

        public static void Label(Rect v, string text, GUIStyle st) => GUI.Label(R(v), text, st);

        /// <summary>Подпись с тенью — читается поверх карты.</summary>
        public static void ShadowLabel(Rect v, string text, GUIStyle st)
        {
            var shadow = new GUIStyle(st) { richText = false };
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(R(new Rect(v.x + 1f, v.y + 1f, v.width, v.height)), StripTags(text), shadow);
            GUI.Label(R(v), text, st);
        }

        private static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<[^>]+>", "");

        public static bool Button(Rect v, string text, GUIStyle st = null) => GUI.Button(R(v), text, st ?? Btn);

        public static float TextWidth(string text, GUIStyle st) => st.CalcSize(new GUIContent(text)).x / S;

        // ---------- Круглые элементы ----------

        /// <summary>Бронзовое кольцо с внутренним ободком и тёмно-бирюзовым полем.</summary>
        public static Texture2D Ring(float virtualSize, Color? inner = null)
        {
            int px = Sz(virtualSize);
            var fill = inner ?? Hex("16302f");
            return Tex($"ring{px}{fill}", () => Paint(px, px, (x, y) =>
            {
                float r = px / 2f;
                var p = new Vector2(x - r, y - r);
                float d = p.magnitude;
                float t = d / r;
                // Свет сверху-слева.
                float light = 0.5f + 0.5f * Vector2.Dot(p.normalized, new Vector2(-0.6f, -0.8f));
                var bronze = Color.Lerp(Shade(Bronze, 0.72f), Hex("e6bd78"), light);
                var c = Color.Lerp(Shade(fill, 1.25f), fill, t);
                float ring = 0.84f, rim = 0.78f;
                c = Color.Lerp(c, BronzeRim, Mathf.Clamp01((d - rim * r) + 0.5f));
                c = Color.Lerp(c, bronze, Mathf.Clamp01((d - ring * r) + 0.5f));
                c = Color.Lerp(c, Shade(BronzeRim, 0.8f), Cov(r - 1.2f * S - d));
                c.a = Cov(d - r + 0.5f);
                return c;
            }));
        }

        /// <summary>Круглое поле (мини-карта): латунное кольцо с прозрачной серединой.</summary>
        public static Texture2D BrassRing(float virtualSize, float innerFraction)
        {
            int px = Sz(virtualSize);
            return Tex($"brass{px}", () => Paint(px, px, (x, y) =>
            {
                float r = px / 2f;
                var p = new Vector2(x - r, y - r);
                float d = p.magnitude;
                float light = 0.5f + 0.5f * Vector2.Dot(p.normalized, new Vector2(-0.6f, -0.8f));
                var brass = Color.Lerp(Hex("8c6a33"), Hex("f0cf8a"), light);
                // Насечки по кольцу.
                float ang = Mathf.Atan2(p.y, p.x);
                brass = Shade(brass, 0.92f + 0.08f * Mathf.Sign(Mathf.Sin(ang * 36f)));
                float innerR = r * innerFraction;
                var c = brass;
                c = Color.Lerp(c, BronzeRim, Cov(Mathf.Abs(d - (innerR + 2f * S)) - 1.2f * S));
                c = Color.Lerp(c, BronzeRim, Cov(Mathf.Abs(d - (r - 1.5f * S)) - 1f * S));
                c.a = Cov(d - r + 0.5f) * Cov(innerR - d);
                return c;
            }));
        }

        /// <summary>Восковая печать в бронзовом кольце с компасной звездой.</summary>
        public static Texture2D Seal(float virtualSize, bool hover)
        {
            int px = Sz(virtualSize);
            return Tex($"seal{px}{hover}", () => Paint(px, px, (x, y) =>
            {
                float r = px / 2f;
                var p = new Vector2(x - r, y - r);
                float d = p.magnitude, ang = Mathf.Atan2(p.y, p.x);
                float light = 0.5f + 0.5f * Vector2.Dot(p.normalized, new Vector2(-0.6f, -0.8f));
                var bronze = Color.Lerp(Shade(Bronze, 0.7f), Hex("efc987"), light);
                var wax = hover ? Hex("a3372a") : Wax;
                float waxR = r * 0.8f + Mathf.Sin(ang * 7f) * r * 0.012f + Mathf.Sin(ang * 13f + 1f) * r * 0.01f;
                var c = bronze;
                c = Color.Lerp(c, BronzeRim, Cov(Mathf.Abs(d - r * 0.86f) - 1.2f * S));
                // Воск: объём от центра к краю.
                var waxCol = Color.Lerp(Shade(wax, 1.15f), Shade(wax, 0.7f), Mathf.Pow(d / waxR, 2f));
                waxCol = Color.Lerp(waxCol, Shade(wax, 1.35f), Mathf.Clamp01(light - 0.6f) * 0.3f * (d / waxR));
                c = Color.Lerp(c, waxCol, Cov(d - waxR));
                // Оттиск: кольцо и компасная звезда.
                var q = p / (r * 0.62f);
                float star = StarSdf(q) * r * 0.62f;
                float ring = (Mathf.Abs(d - r * 0.66f) - 1.1f * S);
                float relief = Mathf.Min(star, ring);
                c = Color.Lerp(c, Shade(wax, 0.55f), Cov(relief + 1.2f * S) * Cov(d - waxR));
                c = Color.Lerp(c, Shade(wax, 1.45f), Cov(relief) * Cov(d - waxR));
                c.a = Cov(d - r + 0.5f);
                return c;
            }));
        }

        private static readonly Vector2[] StarMain = MakeStar(4, 1f, 0.2f, -Mathf.PI / 2f);
        private static readonly Vector2[] StarDiag = MakeStar(4, 0.58f, 0.16f, -Mathf.PI / 4f);

        private static Vector2[] MakeStar(int points, float outer, float inner, float phase)
        {
            var v = new Vector2[points * 2];
            for (int i = 0; i < v.Length; i++)
            {
                float a = phase + i * Mathf.PI / points;
                float rr = i % 2 == 0 ? outer : inner;
                v[i] = new Vector2(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
            }
            return v;
        }

        private static float StarSdf(Vector2 q) => Mathf.Min(Polygon(q, StarMain), Polygon(q, StarDiag));

        /// <summary>Шестиугольная бронзовая кнопка (остриём вверх).</summary>
        public static Texture2D HexButton(float virtualSize, bool hover, bool on)
        {
            int px = Sz(virtualSize);
            return Tex($"hex{px}{hover}{on}", () =>
            {
                float r = px / 2f;
                var outer = HexPoints(r - 1f, r);
                var inner = HexPoints(r * 0.78f, r);
                return Paint(px, px, (x, y) =>
                {
                    var p = new Vector2(x, y);
                    float d = Polygon(p, outer), di = Polygon(p, inner);
                    float light = 0.5f + 0.5f * Vector2.Dot((p - new Vector2(r, r)).normalized, new Vector2(-0.6f, -0.8f));
                    var bronze = Color.Lerp(Shade(Bronze, 0.72f), Hex(hover ? "f6d596" : "e6bd78"), light);
                    var field = on ? Hex("2a5552") : Hex("16302f");
                    field = Color.Lerp(Shade(field, 1.3f), field, (p - new Vector2(r, r)).magnitude / r);
                    var c = Color.Lerp(bronze, field, Cov(di));
                    c = Color.Lerp(c, BronzeRim, Cov(Mathf.Abs(di) - 1.1f * S));
                    c = Color.Lerp(c, BronzeRim, Cov(-d - 1.2f * S));
                    c.a = Cov(d);
                    return c;
                });
            });
        }

        private static Vector2[] HexPoints(float radius, float center)
        {
            var v = new Vector2[6];
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.PI / 3f * i - Mathf.PI / 2f;
                v[i] = new Vector2(center + Mathf.Cos(a) * radius, center + Mathf.Sin(a) * radius);
            }
            return v;
        }

        /// <summary>Бирюзовое кольцо прогресса (0..1) по часовой стрелке от верха.</summary>
        public static Texture2D ProgressRing(float virtualSize, float progress)
        {
            int px = Sz(virtualSize);
            int q = Mathf.Clamp(Mathf.RoundToInt(progress * 64f), 0, 64);
            return Tex($"prog{px}_{q}", () => Paint(px, px, (x, y) =>
            {
                float r = px / 2f;
                var p = new Vector2(x - r, y - r);
                float d = p.magnitude, w = 3.2f * S, mid = r - w - 1f;
                float ang = Mathf.Atan2(p.x, -p.y); // 0 сверху, по часовой
                if (ang < 0f) ang += Mathf.PI * 2f;
                bool done = ang / (Mathf.PI * 2f) <= q / 64f;
                float band = Cov(Mathf.Abs(d - mid) - w);
                var track = new Color(0.05f, 0.1f, 0.1f, 0.75f);
                var c = done ? ProgressColor : track;
                c = Over(new Color(0, 0, 0, 0), c, band);
                c = Over(c, BronzeRim, Cov(Mathf.Abs(d - (mid + w + 0.6f)) - 0.6f * S) * 0.9f);
                return c;
            }));
        }

        // ---------- Иконки ----------

        /// <summary>Белая геометрическая иконка (тонируется цветом при отрисовке).</summary>
        public static Texture2D Glyph(string name, float virtualSize)
        {
            int px = Sz(virtualSize);
            var sdf = GlyphSdf(name);
            return Tex($"g{name}{px}", () => Paint(px, px, (x, y) =>
            {
                var u = new Vector2(x / px * 2f - 1f, y / px * 2f - 1f);
                float d = sdf(u) * px / 2f;
                return new Color(1f, 1f, 1f, Cov(d));
            }));
        }

        public static void Icon(Rect v, string name, Color c) => Draw(v, Glyph(name, v.width), c);

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Func<Vector2, float> GlyphSdf(string name) => name switch
        {
            "gold" => p => Mathf.Min(Mathf.Abs(p.magnitude - 0.68f) - 0.12f, Polygon(p, MakeStar(4, 0.38f, 0.14f, -Mathf.PI / 2f))),
            "science" => p => Mathf.Min(
                Polygon(p, new[] { V(-0.9f, -0.5f), V(-0.07f, -0.3f), V(-0.07f, 0.62f), V(-0.9f, 0.42f) }),
                Polygon(p, new[] { V(0.9f, -0.5f), V(0.07f, -0.3f), V(0.07f, 0.62f), V(0.9f, 0.42f) })),
            "grain" => p =>
            {
                float d = Segment(p, V(0f, 0.9f), V(0f, -0.55f), 0.06f);
                for (int i = 0; i < 4; i++)
                {
                    float y = -0.5f + i * 0.3f;
                    d = Mathf.Min(d, Ellipse(p, V(-0.2f, y), V(0.2f, 0.11f), 0.6f));
                    d = Mathf.Min(d, Ellipse(p, V(0.2f, y), V(0.2f, 0.11f), -0.6f));
                }
                return Mathf.Min(d, Ellipse(p, V(0f, -0.78f), V(0.1f, 0.18f), 0f));
            },
            "reserve" => p =>
            {
                float body = RoundBox(p - V(0f, 0.22f), V(0.82f, 0.48f), 0.08f);
                float lid = RoundBox(p - V(0f, -0.42f), V(0.82f, 0.22f), 0.18f);
                float d = Mathf.Min(body, lid);
                d = Mathf.Max(d, -RoundBox(p - V(0f, -0.2f), V(1f, 0.035f), 0f));
                return Mathf.Max(d, -RoundBox(p - V(0f, 0.02f), V(0.12f, 0.16f), 0.05f));
            },
            "tech" => p =>
            {
                float d = Mathf.Abs(p.magnitude - 0.72f) - 0.08f;
                d = Mathf.Min(d, Mathf.Abs(Ellipse(p, V(0f, 0f), V(0.85f, 0.26f), 0.5f)) - 0.05f);
                d = Mathf.Min(d, Mathf.Abs(Ellipse(p, V(0f, 0f), V(0.85f, 0.26f), -0.5f)) - 0.05f);
                return Mathf.Min(d, Circle(p, V(0f, 0f), 0.18f));
            },
            "treasury" => p =>
            {
                float d = 1f;
                for (int i = 0; i < 3; i++) d = Mathf.Min(d, RoundBox(p - V(-0.15f, 0.55f - i * 0.3f), V(0.62f, 0.12f), 0.12f));
                return Mathf.Min(d, Mathf.Abs(Circle(p, V(0.45f, -0.35f), 0.38f)) - 0.1f);
            },
            "units" => p =>
            {
                float d = Polygon(p, new[] { V(-0.72f, -0.78f), V(0.72f, -0.78f), V(0.72f, 0.02f), V(0f, 0.88f), V(-0.72f, 0.02f) });
                float cross = Mathf.Min(RoundBox(p - V(0f, -0.05f), V(0.09f, 0.55f), 0f), RoundBox(p - V(0f, -0.2f), V(0.45f, 0.09f), 0f));
                return Mathf.Max(d, -cross);
            },
            "diplomacy" => p =>
            {
                // Свиток с печатью.
                float scroll = RoundBox(p - V(-0.1f, -0.1f), V(0.62f, 0.72f), 0.1f);
                scroll = Mathf.Max(scroll, -Mathf.Min(RoundBox(p - V(-0.1f, -0.35f), V(0.4f, 0.05f), 0f), RoundBox(p - V(-0.1f, -0.1f), V(0.4f, 0.05f), 0f)));
                return Mathf.Min(scroll, Circle(p, V(0.45f, 0.5f), 0.32f));
            },
            "star" => StarSdf,
            "people" => p => Mathf.Min(Circle(p, V(0f, -0.45f), 0.3f), RoundBox(p - V(0f, 0.52f), V(0.62f, 0.36f), 0.32f)),
            "water" => p => Mathf.Min(Circle(p, V(0f, 0.28f), 0.52f), Polygon(p, new[] { V(0f, -0.9f), V(0.47f, 0.08f), V(-0.47f, 0.08f) })),
            "food" => p => Mathf.Min(Mathf.Min(Circle(p, V(-0.22f, 0.2f), 0.5f), Circle(p, V(0.22f, 0.2f), 0.5f)),
                Mathf.Min(Segment(p, V(0f, -0.25f), V(0.1f, -0.75f), 0.07f), Ellipse(p, V(0.35f, -0.62f), V(0.28f, 0.12f), -0.5f))),
            "prod" => p => Mathf.Min(Segment(p, V(-0.6f, 0.7f), V(0.2f, -0.1f), 0.1f),
                Polygon(p, new[] { V(-0.05f, -0.75f), V(0.25f, -0.9f), V(0.9f, -0.25f), V(0.75f, 0.05f), V(0.5f, -0.2f), V(0.2f, 0.1f), V(-0.1f, -0.2f), V(0.2f, -0.5f) })),
            "walls" => p =>
            {
                float d = RoundBox(p - V(0f, 0.3f), V(0.85f, 0.52f), 0f);
                for (int i = -1; i <= 1; i++) d = Mathf.Min(d, RoundBox(p - V(i * 0.66f, -0.38f), V(0.19f, 0.2f), 0f));
                return Mathf.Max(d, -Mathf.Min(Circle(p, V(0f, 0.45f), 0.25f), RoundBox(p - V(0f, 0.7f), V(0.25f, 0.2f), 0f)));
            },
            "move" => p => Mathf.Min(Segment(p, V(-0.7f, -0.6f), V(-0.1f, 0f), 0.13f), Mathf.Min(Segment(p, V(-0.1f, 0f), V(-0.7f, 0.6f), 0.13f),
                Mathf.Min(Segment(p, V(0f, -0.6f), V(0.6f, 0f), 0.13f), Segment(p, V(0.6f, 0f), V(0f, 0.6f), 0.13f)))),
            "hp" => p => Mathf.Min(RoundBox(p, V(0.26f, 0.8f), 0.08f), RoundBox(p, V(0.8f, 0.26f), 0.08f)),
            "sword" => p => Mathf.Min(Polygon(p, new[] { V(0.85f, -0.85f), V(0.72f, -0.5f), V(-0.35f, 0.5f), V(-0.5f, 0.35f), V(0.5f, -0.72f) }),
                Mathf.Min(Segment(p, V(-0.62f, 0.2f), V(-0.2f, 0.62f), 0.09f), Segment(p, V(-0.45f, 0.45f), V(-0.8f, 0.8f), 0.1f))),
            "xp" => p => Polygon(p, MakeStar(5, 0.9f, 0.38f, -Mathf.PI / 2f)),
            "build" => p => Mathf.Max(RoundBox(p, V(0.7f, 0.7f), 0.1f), -Mathf.Min(RoundBox(p, V(0.55f, 0.08f), 0f), RoundBox(p, V(0.08f, 0.55f), 0f))),
            _ => p => Circle(p, Vector2.zero, 0.6f),
        };
    }
}
