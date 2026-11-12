using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class YieldModelTests
    {
        [Fact]
        public void DiesPerWaferMatchesTheTextbookExample()
        {
            // 300 mm wafer, 100 mm² die, no edge exclusion: π·150²/100 − π·300/√200 ≈ 640.
            Assert.Equal(640, YieldModel.DiesPerWafer(300, 100, 0));
        }

        [Fact]
        public void EdgeExclusionCostsDies()
        {
            Assert.True(YieldModel.DiesPerWafer(300, 100) < YieldModel.DiesPerWafer(300, 100, 0));
        }

        [Fact]
        public void DegenerateInputsGiveNoDies()
        {
            Assert.Equal(0, YieldModel.DiesPerWafer(300, 0));
            Assert.Equal(0, YieldModel.DiesPerWafer(5, 10));
        }

        [Fact]
        public void PerfectProcessYieldsEverything()
        {
            Assert.Equal(1.0, YieldModel.Yield(0, 100), 12);
        }

        [Fact]
        public void ClusteringIsKinderThanPoisson()
        {
            Assert.True(YieldModel.Yield(2, 100) > YieldModel.PoissonYield(2, 100));
        }

        [Fact]
        public void HugeAlphaApproachesPoisson()
        {
            Assert.Equal(YieldModel.PoissonYield(1, 100), YieldModel.Yield(1, 100, 1e7), 5);
        }

        [Fact]
        public void BiggerDiesYieldWorse()
        {
            double last = 1.0;
            for (int area = 10; area <= 400; area += 10)
            {
                double y = YieldModel.Yield(1.5, area);
                Assert.True(y < last);
                last = y;
            }
        }

        [Fact]
        public void MonteCarloMatchesTheFormula()
        {
            var r = new Rng(42);
            double sampled = YieldModel.SampleYield(r, 40000, 2.0, 80);
            double expected = YieldModel.Yield(2.0, 80);   // about 0.29
            Assert.InRange(sampled, expected - 0.01, expected + 0.01);
        }

        [Fact]
        public void ScribeAddsArea()
        {
            Assert.True(YieldModel.WithScribe(100) > 100);
            Assert.Equal(0.01, YieldModel.WithScribe(0), 9);
        }
    }
}
