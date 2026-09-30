using Runeterra.Core;
using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Units
{
    /// <summary>
    /// Процедурные low-poly фигурки юнитов в двух стилях: сарацины (Айюбиды) и крестоносцы.
    /// Фигурка смотрит вдоль +Z. Оружие — отдельный меш на плечевом шарнире (для анимации удара).
    /// </summary>
    public static class UnitFigure
    {
        public struct Parts
        {
            public Mesh Base;
            public Mesh Body;
            public Mesh Weapon;
            /// <summary>Плечо правой руки в локальных координатах фигурки (уже с масштабом).</summary>
            public Vector3 WeaponPivot;
        }

        private static readonly Color Skin = new Color(0.78f, 0.60f, 0.45f);
        private static readonly Color SkinDark = new Color(0.62f, 0.45f, 0.32f);
        private static readonly Color Steel = new Color(0.74f, 0.76f, 0.80f, 0.35f);
        private static readonly Color SteelDark = new Color(0.40f, 0.42f, 0.46f, 0.5f);
        private static readonly Color Mail = new Color(0.52f, 0.54f, 0.58f);
        private static readonly Color Wood = new Color(0.42f, 0.29f, 0.17f);
        private static readonly Color Leather = new Color(0.36f, 0.24f, 0.14f);
        private static readonly Color Linen = new Color(0.92f, 0.89f, 0.80f);
        private static readonly Color Sand = new Color(0.84f, 0.76f, 0.58f);
        private static readonly Color Straw = new Color(0.86f, 0.74f, 0.42f);
        private static readonly Color CrossRed = new Color(0.78f, 0.10f, 0.10f);
        private static readonly Color Dark = new Color(0.08f, 0.08f, 0.09f);
        private static readonly Color Brass = new Color(0.85f, 0.66f, 0.25f, 0.4f);

        private static readonly Vector3 ShoulderPivot = new Vector3(0.1f, 0.33f, 0f);

        public static Parts Build(UnitData data, RegionData region, float s)
        {
            bool crusader = region.unitStyle == UnitStyle.Crusader;
            var baseFrame = Matrix4x4.Scale(Vector3.one * s);
            // Всадник сидит на коне: фигурка приподнята, конь — часть тела.
            var riderLift = data.mounted ? new Vector3(0f, 0.13f, 0f) : Vector3.zero;
            var frame = baseFrame * Matrix4x4.Translate(riderLift);
            var parts = new Parts { WeaponPivot = (ShoulderPivot + riderLift) * s };

            var baseB = new MeshBuilder { Matrix = baseFrame };
            baseB.Prism(Vector3.zero, 0.3f, 0.035f, 6, region.secondaryColor, region.primaryColor, Mathf.PI / 6f);
            parts.Base = baseB.ToMesh("UnitBase");

            var b = new MeshBuilder { Matrix = baseFrame };
            if (data.mounted) Horse(b, region);
            b.Matrix = frame;
            var w = new MeshBuilder { Matrix = frame };

            // Цвета одежды.
            Color tunic, legs, arms;
            switch (data.role)
            {
                case UnitRole.Civilian:
                    tunic = Color.Lerp(region.primaryColor, Sand, 0.55f);
                    legs = Color.Lerp(Sand, Leather, 0.4f);
                    arms = tunic;
                    break;
                case UnitRole.Melee when crusader:
                    tunic = region.primaryColor; legs = Mail; arms = Mail;
                    break;
                case UnitRole.Melee:
                    tunic = region.primaryColor; legs = Linen; arms = Shade(region.primaryColor, 0.9f);
                    break;
                default:
                    tunic = crusader ? Color.Lerp(region.primaryColor, Sand, 0.35f) : Color.Lerp(region.primaryColor, Linen, 0.35f);
                    legs = crusader ? Shade(Sand, 0.8f) : Linen;
                    arms = tunic;
                    break;
            }

            Body(b, tunic, legs, arms, !crusader);
            if (crusader && data.role == UnitRole.Melee) SurcoatCross(b);
            if (!crusader && data.role == UnitRole.Melee) Cloak(b, frame, Shade(region.primaryColor, 0.7f));
            Headgear(b, frame, data.role, crusader, region);

            switch (data.role)
            {
                case UnitRole.Melee:
                    if (crusader) KiteShield(b, frame, region); else RoundShield(b, frame, region);
                    if (crusader) StraightSword(w, frame); else Scimitar(w, frame);
                    break;
                case UnitRole.Recon:
                    Spear(w, frame);
                    if (!crusader) Buckler(b, frame, region);
                    break;
                case UnitRole.Ranged:
                    Bow(b, frame, crusader);
                    Quiver(b, frame);
                    Arrow(w, frame);
                    break;
                case UnitRole.Civilian:
                    Hammer(w, frame);
                    Sack(b, frame);
                    break;
            }

            // Правая рука — часть оружейного меша, чтобы двигаться вместе с ним.
            w.Matrix = frame * Matrix4x4.Translate(ShoulderPivot);
            w.Box(new Vector3(0f, -0.14f, 0f), new Vector3(0.04f, 0.14f, 0.045f), 0f, arms, arms);
            w.Prism(new Vector3(0f, -0.16f, 0.015f), 0.022f, 0.03f, 6, Skin, Skin);

            parts.Body = b.ToMesh($"UnitBody_{data.id}");
            parts.Weapon = w.ToMesh($"UnitWeapon_{data.id}");
            return parts;
        }

        // ---------- Конь ----------

        private static readonly Color HorseCoat = new Color(0.45f, 0.30f, 0.18f);

        private static void Horse(MeshBuilder b, RegionData region)
        {
            var coat = HorseCoat;
            foreach (float x in new[] { -0.045f, 0.045f })
            foreach (float z in new[] { -0.11f, 0.11f })
                b.Box(new Vector3(x, 0.035f, z), new Vector3(0.035f, 0.1f, 0.035f), 0f, Shade(coat, 0.7f), coat);
            b.Box(new Vector3(0f, 0.12f, 0f), new Vector3(0.12f, 0.09f, 0.32f), 0f, Shade(coat, 0.85f), coat);
            // Попона цвета страны.
            b.Box(new Vector3(0f, 0.14f, -0.01f), new Vector3(0.13f, 0.075f, 0.14f), 0f, Shade(region.primaryColor, 0.8f), region.primaryColor);
            b.Box(new Vector3(0f, 0.17f, 0.16f), new Vector3(0.06f, 0.12f, 0.06f), 0f, coat, coat);
            b.Box(new Vector3(0f, 0.26f, 0.2f), new Vector3(0.05f, 0.05f, 0.11f), 0f, coat, Shade(coat, 1.1f));
            b.Box(new Vector3(0f, 0.2f, 0.13f), new Vector3(0.02f, 0.1f, 0.03f), 0f, Dark, Dark);
            b.Box(new Vector3(0f, 0.1f, -0.17f), new Vector3(0.02f, 0.08f, 0.03f), 0f, Dark, Dark);
        }

        // ---------- Тело ----------

        private static void Body(MeshBuilder b, Color tunic, Color legs, Color arms, bool beard)
        {
            foreach (float x in new[] { -0.035f, 0.035f })
            {
                b.Box(new Vector3(x, 0.035f, 0.01f), new Vector3(0.056f, 0.035f, 0.08f), 0f, Leather, Leather);
                b.Box(new Vector3(x, 0.06f, 0f), new Vector3(0.05f, 0.13f, 0.055f), 0f, legs, legs);
            }
            b.Cone(new Vector3(0f, 0.11f, 0f), 0.092f, 0.09f, 8, Shade(tunic, 0.8f), Shade(tunic, 0.92f), 0.078f);
            b.Cone(new Vector3(0f, 0.19f, 0f), 0.08f, 0.15f, 8, Shade(tunic, 0.92f), tunic, 0.062f);
            b.Prism(new Vector3(0f, 0.2f, 0f), 0.084f, 0.02f, 8, Leather, Leather);
            b.Prism(new Vector3(0f, 0.33f, 0f), 0.074f, 0.022f, 8, Shade(arms, 0.95f), arms);
            b.Box(new Vector3(-0.1f, 0.2f, 0f), new Vector3(0.04f, 0.14f, 0.045f), 0f, arms, arms);
            b.Prism(new Vector3(-0.1f, 0.18f, 0.015f), 0.022f, 0.03f, 6, Skin, Skin);
            b.Prism(new Vector3(0f, 0.35f, 0f), 0.042f, 0.06f, 8, Skin, Skin);
            if (beard) b.Box(new Vector3(0f, 0.34f, 0.03f), new Vector3(0.056f, 0.035f, 0.025f), 0f, Dark, Dark);
        }

        private static void SurcoatCross(MeshBuilder b)
        {
            b.Box(new Vector3(0f, 0.14f, 0.083f), new Vector3(0.024f, 0.17f, 0.008f), 0f, CrossRed, CrossRed);
            b.Box(new Vector3(0f, 0.255f, 0.075f), new Vector3(0.09f, 0.024f, 0.008f), 0f, CrossRed, CrossRed);
        }

        private static void Cloak(MeshBuilder b, Matrix4x4 frame, Color color)
        {
            At(b, frame, new Vector3(0f, 0.09f, -0.078f), Quaternion.Euler(-9f, 0f, 0f));
            b.Box(Vector3.zero, new Vector3(0.15f, 0.25f, 0.014f), 0f, color, color);
            b.Matrix = frame;
        }

        private static void Headgear(MeshBuilder b, Matrix4x4 frame, UnitRole role, bool crusader, RegionData region)
        {
            var top = new Vector3(0f, 0.395f, 0f);
            if (role == UnitRole.Civilian)
            {
                b.Prism(top, 0.08f, 0.01f, 10, Straw, Straw);
                b.Cone(top + Vector3.up * 0.01f, 0.045f, 0.045f, 8, Straw, Shade(Straw, 1.1f));
                return;
            }
            if (crusader)
            {
                if (role == UnitRole.Melee)
                {
                    // Горшковый шлем с прорезью для глаз.
                    b.Prism(new Vector3(0f, 0.335f, 0f), 0.052f, 0.088f, 8, Steel, Steel);
                    b.Box(new Vector3(0f, 0.378f, 0.047f), new Vector3(0.075f, 0.011f, 0.012f), 0f, Dark, Dark);
                    b.Box(new Vector3(0f, 0.345f, 0.048f), new Vector3(0.011f, 0.03f, 0.01f), 0f, Dark, Dark);
                }
                else
                {
                    // Шапель — шлем с широкими полями.
                    b.Prism(top, 0.075f, 0.01f, 10, SteelDark, Steel);
                    b.Dome(top + Vector3.up * 0.005f, 0.046f, 10, 3, Steel, Steel);
                }
                return;
            }
            // Сарацины: тюрбан; у воина — шлем со шпилем, у разведчика — куфия.
            if (role == UnitRole.Recon)
            {
                b.Cone(new Vector3(0f, 0.33f, 0f), 0.062f, 0.085f, 8, Sand, Shade(Sand, 1.05f), 0.046f);
                b.Prism(top - Vector3.up * 0.012f, 0.05f, 0.016f, 8, Dark, Dark);
                return;
            }
            b.Prism(new Vector3(0f, 0.375f, 0f), 0.056f, 0.024f, 10, Shade(Linen, 0.92f), Linen);
            b.Dome(top, 0.056f, 10, 3, Linen, Linen);
            if (role == UnitRole.Melee) b.Cone(top + Vector3.up * 0.05f, 0.016f, 0.055f, 6, Steel, Steel);
            b.Box(new Vector3(0.03f, 0.39f, 0.045f), new Vector3(0.02f, 0.02f, 0.01f), 0f, region.secondaryColor, region.secondaryColor);
        }

        // ---------- Щиты ----------

        private static void RoundShield(MeshBuilder b, Matrix4x4 frame, RegionData region)
        {
            At(b, frame, new Vector3(-0.12f, 0.22f, 0.06f), Quaternion.Euler(90f, 0f, 0f));
            b.Prism(Vector3.down * 0.004f, 0.094f, 0.012f, 14, region.secondaryColor, region.secondaryColor);
            b.Prism(Vector3.zero, 0.085f, 0.016f, 14, Wood, Shade(Wood, 1.15f));
            b.Dome(Vector3.up * 0.016f, 0.026f, 8, 3, Brass, Brass);
            b.Matrix = frame;
        }

        private static void Buckler(MeshBuilder b, Matrix4x4 frame, RegionData region)
        {
            At(b, frame, new Vector3(-0.12f, 0.2f, 0.05f), Quaternion.Euler(90f, 0f, 0f));
            b.Prism(Vector3.zero, 0.05f, 0.012f, 10, region.secondaryColor, Shade(Wood, 1.1f));
            b.Dome(Vector3.up * 0.012f, 0.016f, 8, 2, Brass, Brass);
            b.Matrix = frame;
        }

        private static void KiteShield(MeshBuilder b, Matrix4x4 frame, RegionData region)
        {
            At(b, frame, new Vector3(-0.12f, 0.1f, 0.07f), Quaternion.identity);
            var white = Shade(region.primaryColor, 1.02f);
            Vector3 tl = new Vector3(-0.065f, 0.24f, 0f), tr = new Vector3(0.065f, 0.24f, 0f);
            Vector3 mr = new Vector3(0.065f, 0.15f, 0f), ml = new Vector3(-0.065f, 0.15f, 0f), bot = new Vector3(0f, 0f, 0f);
            b.Quad(tl, tr, mr, ml, white, Vector3.forward);
            b.Triangle(ml, mr, bot, white, Vector3.forward);
            var back = Vector3.back * 0.012f;
            b.Quad(tl + back, tr + back, mr + back, ml + back, Wood, Vector3.back);
            b.Triangle(ml + back, mr + back, bot + back, Wood, Vector3.back);
            b.Box(new Vector3(0f, 0.03f, 0.003f), new Vector3(0.02f, 0.2f, 0.006f), 0f, CrossRed, CrossRed);
            b.Box(new Vector3(0f, 0.165f, 0.003f), new Vector3(0.11f, 0.02f, 0.006f), 0f, CrossRed, CrossRed);
            b.Matrix = frame;
        }

        // ---------- Оружие (в координатах плеча) ----------

        private static Vector3 Hand => new Vector3(0f, -0.16f, 0.03f);

        private static void Scimitar(MeshBuilder w, Matrix4x4 frame)
        {
            var arm = frame * Matrix4x4.Translate(ShoulderPivot);
            Rod(w, arm, Hand, Vector3.down + Vector3.back * 0.3f, 0.045f, 0.014f, 0.014f, Leather);
            Rod(w, arm, Hand - new Vector3(0.035f, 0f, 0f), Vector3.right, 0.07f, 0.012f, 0.012f, Brass);
            // Изогнутый клинок из сегментов.
            var p = Hand;
            var dir = new Vector3(0f, 0.55f, 1f).normalized;
            for (int i = 0; i < 5; i++)
            {
                Rod(w, arm, p, dir, 0.052f, 0.006f, 0.024f - i * 0.002f, Steel);
                p += dir * 0.05f;
                dir = Quaternion.Euler(-13f, 0f, 0f) * dir;
            }
            w.Matrix = frame;
        }

        private static void StraightSword(MeshBuilder w, Matrix4x4 frame)
        {
            var arm = frame * Matrix4x4.Translate(ShoulderPivot);
            var dir = new Vector3(0f, 0.55f, 1f).normalized;
            Rod(w, arm, Hand - dir * 0.05f, dir, 0.05f, 0.014f, 0.014f, Leather);
            Rod(w, arm, Hand - new Vector3(0.05f, 0f, 0f), Vector3.right, 0.1f, 0.014f, 0.014f, Steel);
            Rod(w, arm, Hand, dir, 0.26f, 0.007f, 0.024f, Steel);
            w.Matrix = frame;
        }

        private static void Spear(MeshBuilder w, Matrix4x4 frame)
        {
            var arm = frame * Matrix4x4.Translate(ShoulderPivot);
            var dir = new Vector3(0f, 1f, 0.28f).normalized;
            Rod(w, arm, Hand - dir * 0.16f, dir, 0.56f, 0.013f, 0.013f, Wood);
            At(w, arm, Hand + dir * 0.4f, Quaternion.FromToRotation(Vector3.up, dir));
            w.Cone(Vector3.zero, 0.022f, 0.07f, 4, Steel, Steel);
            w.Matrix = frame;
        }

        private static void Arrow(MeshBuilder w, Matrix4x4 frame)
        {
            var arm = frame * Matrix4x4.Translate(ShoulderPivot);
            Rod(w, arm, Hand - new Vector3(0f, 0f, 0.04f), new Vector3(0f, 0.25f, 1f), 0.16f, 0.006f, 0.006f, Wood);
            w.Matrix = frame;
        }

        private static void Hammer(MeshBuilder w, Matrix4x4 frame)
        {
            var arm = frame * Matrix4x4.Translate(ShoulderPivot);
            var dir = new Vector3(0f, 0.9f, 0.45f).normalized;
            Rod(w, arm, Hand - dir * 0.03f, dir, 0.2f, 0.014f, 0.014f, Wood);
            At(w, arm, Hand + dir * 0.17f, Quaternion.FromToRotation(Vector3.up, dir));
            w.Box(Vector3.zero, new Vector3(0.045f, 0.045f, 0.09f), 0f, SteelDark, Steel);
            w.Matrix = frame;
        }

        // ---------- Снаряжение на теле ----------

        private static void Bow(MeshBuilder b, Matrix4x4 frame, bool longbow)
        {
            // Дуга в плоскости YZ в левой руке; у сарацинов — короче, с загнутыми концами.
            var grip = new Vector3(-0.12f, 0.2f, 0.06f);
            float half = longbow ? 0.2f : 0.15f;
            int n = 8;
            Vector3 prev = default, first = default, last = default;
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Lerp(-1f, 1f, (float)i / n);
                float bend = longbow ? 0.05f * (1f - t * t) : 0.05f * (1f - t * t) - 0.025f * Mathf.Pow(Mathf.Abs(t), 6f);
                var p = grip + new Vector3(0f, t * half, bend);
                if (i == 0) first = p;
                if (i > 0) Rod(b, frame, prev, p - prev, (p - prev).magnitude, 0.012f, 0.012f, Wood);
                prev = p;
                last = p;
            }
            Rod(b, frame, first, last - first, (last - first).magnitude, 0.003f, 0.003f, Linen);
            b.Matrix = frame;
        }

        private static void Quiver(MeshBuilder b, Matrix4x4 frame)
        {
            At(b, frame, new Vector3(0.04f, 0.17f, -0.085f), Quaternion.Euler(-15f, 0f, -18f));
            b.Prism(Vector3.zero, 0.03f, 0.17f, 6, Leather, Leather);
            for (int i = 0; i < 3; i++)
                b.Box(new Vector3((i - 1) * 0.012f, 0.17f, 0f), new Vector3(0.006f, 0.05f, 0.012f), 0f, Linen, Linen);
            b.Matrix = frame;
        }

        private static void Sack(MeshBuilder b, Matrix4x4 frame)
        {
            At(b, frame, new Vector3(-0.02f, 0.17f, -0.09f), Quaternion.Euler(-10f, 0f, 0f));
            b.Prism(Vector3.zero, 0.055f, 0.11f, 7, Shade(Sand, 0.8f), Shade(Sand, 0.9f));
            b.Matrix = frame;
        }

        // ---------- Утилиты ----------

        private static void At(MeshBuilder m, Matrix4x4 frame, Vector3 pos, Quaternion rot) =>
            m.Matrix = frame * Matrix4x4.TRS(pos, rot, Vector3.one);

        /// <summary>Брусок длиной len от точки from вдоль dir.</summary>
        private static void Rod(MeshBuilder m, Matrix4x4 frame, Vector3 from, Vector3 dir, float len, float w, float d, Color c)
        {
            At(m, frame, from, Quaternion.FromToRotation(Vector3.up, dir.normalized));
            m.Box(Vector3.zero, new Vector3(w, len, d), 0f, c, c);
            m.Matrix = frame;
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);
    }
}
