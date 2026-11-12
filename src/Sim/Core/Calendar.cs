using System;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Game time. One tick is one week; a month is 4 ticks and a year is 48, so months and quarters line up exactly.
    /// Tick 0 is the first week of January <see cref="StartYear"/>.
    /// </summary>
    public static class Calendar
    {
        public const int StartYear = 1970;
        public const int WeeksPerMonth = 4;
        public const int WeeksPerYear = WeeksPerMonth * 12;

        public static readonly string[] MonthNames = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        /// <summary>Tick at the start of the given year and month (month 1-12).</summary>
        public static int TickOf(int year, int month = 1, int week = 0) => (year - StartYear) * WeeksPerYear + (month - 1) * WeeksPerMonth + week;

        /// <summary>Months since January of the start year.</summary>
        public static int MonthIndex(int tick) => FloorDiv(tick, WeeksPerMonth);
        public static int YearOf(int tick) => StartYear + FloorDiv(tick, WeeksPerYear);
        public static double YearF(int tick) => StartYear + tick / (double)WeeksPerYear;

        /// <summary>0 = January ... 11 = December.</summary>
        public static int MonthInYear(int tick) => Mod(MonthIndex(tick), 12);
        public static int WeekInMonth(int tick) => Mod(tick, WeeksPerMonth);
        public static int Quarter(int tick) => MonthInYear(tick) / 3 + 1;

        public static bool IsMonthEnd(int tick) => WeekInMonth(tick) == WeeksPerMonth - 1;
        public static bool IsQuarterEnd(int tick) => IsMonthEnd(tick) && MonthInYear(tick) % 3 == 2;
        public static bool IsYearEnd(int tick) => IsMonthEnd(tick) && MonthInYear(tick) == 11;

        public static string DateLabel(int tick) => MonthNames[MonthInYear(tick)] + " " + YearOf(tick);
        public static string WeekLabel(int tick) => $"Week {WeekInMonth(tick) + 1}, {DateLabel(tick)}";
        public static string QuarterLabel(int tick) => $"Q{Quarter(tick)} {YearOf(tick)}";

        /// <summary>Piecewise-linear lookup in a table sorted by x. (From Console Tycoon's Catalog.Lerp.)</summary>
        public static double Lerp((double x, double y)[] t, double x)
        {
            if (x <= t[0].x) return t[0].y;
            for (int i = 1; i < t.Length; i++)
            {
                if (x <= t[i].x)
                {
                    double f = (x - t[i - 1].x) / (t[i].x - t[i - 1].x);
                    return t[i - 1].y + (t[i].y - t[i - 1].y) * f;
                }
            }
            return t[t.Length - 1].y;
        }

        // Approximate US price level relative to 1970 (tuning values, not sourced). Data files price things in 1970 dollars.
        static readonly (double, double)[] PriceLevel =
        {
            (1970, 1.00), (1975, 1.39), (1980, 2.12), (1985, 2.77), (1990, 3.36), (1995, 3.90),
            (2000, 4.43), (2005, 5.01), (2010, 5.64), (2015, 6.10), (2020, 6.61), (2022, 7.65), (2026, 8.30),
        };

        /// <summary>Multiplier from 1970 dollars to nominal dollars for the given year.</summary>
        public static double Inflation(double year) => Lerp(PriceLevel, year);

        static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);
        static int Mod(int a, int b) => ((a % b) + b) % b;
    }
}
