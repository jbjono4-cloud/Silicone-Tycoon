using System;

namespace SiliconeTycoon.Sim
{
    /// <summary>Text formatting shared by the sim's messages and the UI. (Money and Big come from Console Tycoon's Ui.)</summary>
    public static class Format
    {
        public static string Money(double v) => (v < 0 ? "-$" : "$") + Math.Abs(v).ToString("N0");

        public static string Big(double v)
        {
            double a = Math.Abs(v);
            string sign = v < 0 ? "-" : "";
            if (a >= 1e9) return sign + "$" + (a / 1e9).ToString("0.00") + "B";
            if (a >= 1e6) return sign + "$" + (a / 1e6).ToString("0.0") + "M";
            if (a >= 1e3) return sign + "$" + (a / 1e3).ToString("0") + "k";
            return sign + "$" + a.ToString("0");
        }

        public static string Pct(double v) => (v * 100).ToString("0.0") + "%";

        /// <summary>A count of things: 950, 12k, 3.4M.</summary>
        public static string Units(double v)
        {
            if (v >= 1e6) return (v / 1e6).ToString("0.0") + "M";
            if (v >= 1e4) return (v / 1e3).ToString("0") + "k";
            return v.ToString("N0");
        }
    }
}
