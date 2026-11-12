using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class CalendarTests
    {
        [Fact]
        public void TicksMapToDates()
        {
            Assert.Equal(0, Calendar.TickOf(1970, 1));
            Assert.Equal(48, Calendar.TickOf(1971, 1));
            Assert.Equal(1970, Calendar.YearOf(47));
            Assert.Equal(1971, Calendar.YearOf(48));
            Assert.Equal(6, Calendar.MonthInYear(Calendar.TickOf(1975, 7)));
            Assert.Equal("Mar 1983", Calendar.DateLabel(Calendar.TickOf(1983, 3, 2)));
            Assert.Equal("Q3 1975", Calendar.QuarterLabel(Calendar.TickOf(1975, 8)));
        }

        [Fact]
        public void MonthQuarterAndYearEnds()
        {
            Assert.True(Calendar.IsMonthEnd(3));
            Assert.False(Calendar.IsMonthEnd(4));
            Assert.True(Calendar.IsQuarterEnd(Calendar.TickOf(1970, 3, 3)));
            Assert.False(Calendar.IsQuarterEnd(Calendar.TickOf(1970, 4, 3)));
            Assert.True(Calendar.IsYearEnd(47));
            Assert.False(Calendar.IsYearEnd(43));
        }

        [Fact]
        public void LerpClampsAndInterpolates()
        {
            var t = new (double, double)[] { (0, 10), (10, 20) };
            Assert.Equal(10, Calendar.Lerp(t, -5));
            Assert.Equal(15, Calendar.Lerp(t, 5), 9);
            Assert.Equal(20, Calendar.Lerp(t, 50));
        }

        [Fact]
        public void InflationStartsAtOneAndRises()
        {
            Assert.Equal(1.0, Calendar.Inflation(1970), 9);
            double last = 0;
            for (int y = 1970; y <= 2026; y++)
            {
                double v = Calendar.Inflation(y);
                Assert.True(v >= last, $"Inflation fell in {y}");
                last = v;
            }
        }
    }
}
