namespace Runeterra.Core
{
    /// <summary>
    /// Степень на одних сложениях, умножениях и делениях double: они по IEEE 754 округляются одинаково
    /// на любой платформе, в отличие от Math.Pow/Exp/Log из библиотеки C. Нужна правилам,
    /// где результат влияет на ход партии (цена товара).
    /// </summary>
    public static class DetMath
    {
        private const double Ln2 = 0.69314718055994530942;

        /// <summary>x^y для x &gt; 0 (для x ≤ 0 — 0).</summary>
        public static double Pow(double x, double y)
        {
            if (x <= 0.0) return 0.0;
            if (y == 0.0 || x == 1.0) return 1.0;
            return Exp(y * Ln(x));
        }

        /// <summary>Натуральный логарифм: x = m·2^e, m в [1, 2), ln m через ряд atanh.</summary>
        public static double Ln(double x)
        {
            int e = 0;
            while (x >= 2.0) { x *= 0.5; e++; }
            while (x < 1.0) { x *= 2.0; e--; }
            double t = (x - 1.0) / (x + 1.0), t2 = t * t, term = t, sum = 0.0;
            for (int k = 1; k < 60; k += 2)
            {
                sum += term / k;
                term *= t2;
            }
            return 2.0 * sum + e * Ln2;
        }

        /// <summary>Экспонента: y = k·ln2 + r, |r| ≤ ln2/2, e^r рядом Тейлора.</summary>
        public static double Exp(double y)
        {
            if (y > 700.0) y = 700.0;
            if (y < -700.0) return 0.0;
            int k = (int)(y / Ln2 + (y >= 0.0 ? 0.5 : -0.5));
            double r = y - k * Ln2, term = 1.0, sum = 1.0;
            for (int n = 1; n < 30; n++)
            {
                term *= r / n;
                sum += term;
            }
            while (k > 0) { sum *= 2.0; k--; }
            while (k < 0) { sum *= 0.5; k++; }
            return sum;
        }
    }
}
