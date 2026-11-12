using System;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Seeded random numbers (xoshiro256**). Its whole state is four public fields, so it saves with the game and a
    /// loaded game continues exactly where it left off. Never use System.Random inside the sim.
    /// </summary>
    public class Rng
    {
        public ulong S0;
        public ulong S1;
        public ulong S2;
        public ulong S3;

        /// <summary>For the JSON loader only.</summary>
        public Rng() { }

        public Rng(ulong seed)
        {
            ulong x = seed;
            S0 = SplitMix(ref x);
            S1 = SplitMix(ref x);
            S2 = SplitMix(ref x);
            S3 = SplitMix(ref x);
        }

        static ulong SplitMix(ref ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        static ulong Rotl(ulong v, int k) => (v << k) | (v >> (64 - k));

        public ulong NextULong()
        {
            ulong result = Rotl(S1 * 5, 7) * 9;
            ulong t = S1 << 17;
            S2 ^= S0;
            S3 ^= S1;
            S1 ^= S2;
            S0 ^= S3;
            S2 ^= t;
            S3 = Rotl(S3, 45);
            return result;
        }

        /// <summary>Uniform in [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return (int)(NextDouble() * maxExclusive);
        }

        public int Next(int min, int maxExclusive) => min + Next(maxExclusive - min);

        public bool Chance(double p) => NextDouble() < p;

        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Normal distribution (Box-Muller).</summary>
        public double Normal(double mean = 0, double sd = 1)
        {
            double u1 = 1.0 - NextDouble();
            double u2 = NextDouble();
            return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Gamma distribution with the given shape and scale (Marsaglia-Tsang). Mean = shape × scale.</summary>
        public double Gamma(double shape, double scale)
        {
            if (shape <= 0 || scale <= 0) return 0;
            if (shape < 1)
            {
                double u = 1.0 - NextDouble();
                return Gamma(shape + 1, scale) * Math.Pow(u, 1.0 / shape);
            }
            double d = shape - 1.0 / 3.0;
            double c = 1.0 / Math.Sqrt(9.0 * d);
            while (true)
            {
                double x, v;
                do
                {
                    x = Normal();
                    v = 1.0 + c * x;
                } while (v <= 0);
                v = v * v * v;
                double u = 1.0 - NextDouble();
                if (u < 1.0 - 0.0331 * x * x * x * x) return d * v * scale;
                if (Math.Log(u) < 0.5 * x * x + d * (1.0 - v + Math.Log(v))) return d * v * scale;
            }
        }

        /// <summary>Poisson-distributed count with the given mean.</summary>
        public int Poisson(double lambda)
        {
            if (lambda <= 0) return 0;
            if (lambda < 30)
            {
                double limit = Math.Exp(-lambda);
                int k = 0;
                double p = 1.0;
                do
                {
                    k++;
                    p *= NextDouble();
                } while (p > limit);
                return k - 1;
            }
            return Math.Max(0, (int)Math.Round(Normal(lambda, Math.Sqrt(lambda))));
        }
    }
}
