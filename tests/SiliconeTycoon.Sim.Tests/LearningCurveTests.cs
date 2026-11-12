using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class LearningCurveTests
    {
        static ProcessNodeDef Node => TestData.Load().Node("n10um");

        [Fact]
        public void StartsAtLaunchAndSettlesAtMature()
        {
            Assert.Equal(Node.D0Launch, LearningCurve.D0(Node, 0), 9);
            Assert.Equal(Node.D0Mature, LearningCurve.D0(Node, 1000), 6);
        }

        [Fact]
        public void DefectDensityOnlyFalls()
        {
            double last = double.MaxValue;
            for (int m = 0; m <= 120; m++)
            {
                double d0 = LearningCurve.D0(Node, m);
                Assert.True(d0 < last);
                last = d0;
            }
        }

        [Fact]
        public void BusyFabsLearnFaster()
        {
            Assert.True(LearningCurve.Rate(1.0, 1.0) > LearningCurve.Rate(0.3, 1.0));
            Assert.True(LearningCurve.Rate(0, 1.0) > 0);
        }

        [Fact]
        public void EngineeringBudgetScalesLearning()
        {
            Assert.Equal(0.7, LearningCurve.EngineerFactor(0, 100), 9);
            Assert.Equal(1.0, LearningCurve.EngineerFactor(100, 100), 9);
            Assert.Equal(1.3, LearningCurve.EngineerFactor(200, 100), 9);
            Assert.Equal(1.3, LearningCurve.EngineerFactor(1000, 100), 9);
        }
    }
}
