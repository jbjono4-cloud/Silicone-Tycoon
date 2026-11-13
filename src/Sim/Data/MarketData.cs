using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>What a market segment judges a chip on.</summary>
    public enum Metric { Control, Compute, Graphics }

    /// <summary>Lookup tables written as [[year, value], ...] in the data files.</summary>
    public static class Curves
    {
        public static double At(double[][] t, double x)
        {
            if (t == null || t.Length == 0) return 0;
            if (x <= t[0][0]) return t[0][1];
            for (int i = 1; i < t.Length; i++)
            {
                if (x <= t[i][0])
                {
                    double f = (x - t[i - 1][0]) / (t[i][0] - t[i - 1][0]);
                    return t[i - 1][1] + (t[i][1] - t[i - 1][1]) * f;
                }
            }
            return t[t.Length - 1][1];
        }

        public static bool Valid(double[][] t)
        {
            if (t == null || t.Length == 0) return false;
            for (int i = 0; i < t.Length; i++)
            {
                if (t[i] == null || t[i].Length != 2 || t[i][1] < 0) return false;
                if (i > 0 && t[i][0] <= t[i - 1][0]) return false;
            }
            return true;
        }
    }

    public class FeatureWant
    {
        public string Feature;
        /// <summary>Year buyers start to care.</summary>
        public int From;
        /// <summary>Share of a chip's appeal lost without it.</summary>
        public double Weight;
        /// <summary>Year it becomes a must-have (0 = never).</summary>
        public int RequiredFrom;
    }

    /// <summary>A feature a particular customer insists on from a year.</summary>
    public class FeatureNeed
    {
        public string Feature;
        public int From;
    }

    /// <summary>A market for chips. Demand is chips per month; prices are 1970 dollars per chip.</summary>
    public class SegmentDef
    {
        public string Id;
        public string Name;
        public int From = 1970;
        public Metric Metric;
        public double MaxPowerW = 2;
        /// <summary>Most package pins typical boards in this market take.</summary>
        public int MaxPins = 999;
        /// <summary>Share of demand bought through distributors (the open market) rather than design-win contracts.</summary>
        public double OpenShare = 0.3;
        public double PriceSensitivity = 1.2;
        public double PerfWeight = 1.0;
        public double[][] Demand;
        public double[][] Price;
        /// <summary>Performance a competitive chip delivers in that year, in the segment's metric.</summary>
        public double[][] Need;
        /// <summary>Features buyers in this market look for: missing one costs score; from RequiredFrom it is a must.</summary>
        public List<FeatureWant> Wants = new List<FeatureWant>();

        public bool Active(double year) => year >= From;
        public IEnumerable<FeatureWant> WantsAt(double year) => Wants.Where(w => year >= w.From);
        public IEnumerable<string> RequiredAt(double year) => Wants.Where(w => w.RequiredFrom > 0 && year >= w.RequiredFrom).Select(w => w.Feature);
        public double DemandAt(double year) => Active(year) ? Curves.At(Demand, year) : 0;
        public double PriceAt(double year) => Curves.At(Price, year);
        public double NeedAt(double year) => Curves.At(Need, year);
    }

    /// <summary>A company that buys chips through design-win contracts.</summary>
    public class CustomerDef
    {
        public string Id;
        public string Name;
        public string SegmentId;
        public string Country;
        public int From = 1970;
        public int To = 9999;
        /// <summary>Share of the segment's demand this customer buys.</summary>
        public double Share;
        /// <summary>Length of a product generation, and so of each contract.</summary>
        public int CycleMonths = 24;
        /// <summary>Months from awarding a contract to the first deliveries.</summary>
        public int LeadMonths = 6;
        public double PriceSensitivity = 1;
        /// <summary>Score bonus for the current supplier.</summary>
        public double Loyalty = 0.1;
        /// <summary>Most package pins the customer's board takes.</summary>
        public int MaxPins = 999;
        /// <summary>Features this customer insists on beyond what its market requires.</summary>
        public List<FeatureNeed> Needs = new List<FeatureNeed>();
        /// <summary>Lowest clock its product works at, [[year, MHz], ...]; null = no clock requirement.</summary>
        public double[][] MinClock;

        public bool Active(int year) => year >= From && year <= To;
    }

    /// <summary>A rival chip company. Fictional; runs the same design maths as the player.</summary>
    public class RivalDef
    {
        public string Id;
        public string Name;
        public string Country;
        /// <summary>Years behind the historical node schedule.</summary>
        public double NodeLagYears;
        /// <summary>Normal price as a share of the segment's going price. Below 1 = undercuts the market.</summary>
        public double PriceFactor = 1.0;
        public double Rep;
        /// <summary>Starting capacity as a share of the demand in its segments.</summary>
        public double StartShare = 0.2;
        /// <summary>Most of its segments' demand it will build capacity for.</summary>
        public double MaxShare = 0.4;
        public int Engineers = 8;
        public List<string> Segments = new List<string>();
    }
}
