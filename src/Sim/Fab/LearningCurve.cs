using System;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Defect density falls as a fab learns its process: D0(t) = D0mature + (D0launch − D0mature)·e^(−t/τ).
    /// Here t is "learning", measured in months of normal-volume production, so a full, well-staffed fab learns faster.
    /// </summary>
    public static class LearningCurve
    {
        public static double D0(ProcessNodeDef n, double learning) =>
            n.D0Mature + (n.D0Launch - n.D0Mature) * Math.Exp(-Math.Max(0, learning) / n.LearningMonths);

        /// <summary>
        /// Months of learning gained per month of running. Utilisation is wafer starts over available capacity (0-1);
        /// an idle fab still learns a little from test wafers.
        /// </summary>
        public static double Rate(double utilisation, double engineerFactor) =>
            (0.4 + 0.6 * Math.Clamp(utilisation, 0, 1)) * engineerFactor;

        /// <summary>
        /// How much the engineering budget speeds learning: 0.7 with no budget, 1.0 at the recommended level, up to 1.3
        /// at double it.
        /// </summary>
        public static double EngineerFactor(double budget, double recommended)
        {
            if (recommended <= 0) return 1;
            double r = Math.Clamp(budget / recommended, 0, 2);
            return r <= 1 ? 0.7 + 0.3 * r : 1.0 + 0.3 * (r - 1);
        }
    }
}
