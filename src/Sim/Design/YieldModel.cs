using System;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Dies per wafer and yield. Defect density D0 is in defects per cm²; die areas are in mm².
    /// The analytic yield uses a negative binomial (clustered defects); lots are simulated by Monte Carlo with the same
    /// clustering, so a sampled lot averages out to the formula.
    /// </summary>
    public static class YieldModel
    {
        /// <summary>Defect clustering factor. Lower = more clustered = kinder to big dies.</summary>
        public const double DefaultAlpha = 2.5;
        /// <summary>Unusable ring around the wafer edge.</summary>
        public const double EdgeExclusionMm = 3;
        /// <summary>Width of the saw street between dies.</summary>
        public const double ScribeMm = 0.1;

        /// <summary>Gross dies per wafer for a die of the given area (scribe included), using the usual edge-loss approximation.</summary>
        public static int DiesPerWafer(double waferMm, double dieAreaMm2, double edgeExclusionMm = EdgeExclusionMm)
        {
            if (dieAreaMm2 <= 0) return 0;
            double d = waferMm - 2 * edgeExclusionMm;
            if (d <= 0) return 0;
            double n = Math.PI * d * d / (4 * dieAreaMm2) - Math.PI * d / Math.Sqrt(2 * dieAreaMm2);
            return Math.Max(0, (int)Math.Floor(n));
        }

        /// <summary>Footprint of a square die on the wafer, including its share of the saw street.</summary>
        public static double WithScribe(double dieAreaMm2)
        {
            double side = Math.Sqrt(Math.Max(0, dieAreaMm2)) + ScribeMm;
            return side * side;
        }

        /// <summary>Simple Poisson yield, e^(−D0·A). Shown in engineering views for comparison.</summary>
        public static double PoissonYield(double d0PerCm2, double dieAreaMm2) => Math.Exp(-d0PerCm2 * dieAreaMm2 / 100.0);

        /// <summary>Clustered-defect yield, (1 + D0·A/α)^−α.</summary>
        public static double Yield(double d0PerCm2, double dieAreaMm2, double alpha = DefaultAlpha)
        {
            double mean = d0PerCm2 * dieAreaMm2 / 100.0;
            return Math.Pow(1.0 + mean / alpha, -alpha);
        }

        /// <summary>Defect count for each sampled die. Each die's defect rate is drawn from a gamma distribution, then defects from a Poisson.</summary>
        public static int[] SampleDefects(Rng rng, int dies, double d0PerCm2, double dieAreaMm2, double alpha = DefaultAlpha)
        {
            var result = new int[Math.Max(0, dies)];
            double mean = d0PerCm2 * dieAreaMm2 / 100.0;
            if (mean <= 0) return result;
            for (int i = 0; i < result.Length; i++)
            {
                double lambda = rng.Gamma(alpha, mean / alpha);
                result[i] = rng.Poisson(lambda);
            }
            return result;
        }

        /// <summary>Fraction of defect-free dies in a sample of the given size.</summary>
        public static double SampleYield(Rng rng, int dies, double d0PerCm2, double dieAreaMm2, double alpha = DefaultAlpha)
        {
            if (dies <= 0) return 0;
            var defects = SampleDefects(rng, dies, d0PerCm2, dieAreaMm2, alpha);
            int good = 0;
            foreach (var d in defects) if (d == 0) good++;
            return good / (double)dies;
        }
    }
}
