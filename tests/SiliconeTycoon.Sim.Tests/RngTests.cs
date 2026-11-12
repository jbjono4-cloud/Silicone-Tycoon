using System;
using System.Text.Json;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class RngTests
    {
        [Fact]
        public void SameSeedSameSequence()
        {
            var a = new Rng(99);
            var b = new Rng(99);
            for (int i = 0; i < 100; i++) Assert.Equal(a.NextULong(), b.NextULong());
        }

        [Fact]
        public void DifferentSeedsDiffer()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            int same = 0;
            for (int i = 0; i < 100; i++) if (a.NextULong() == b.NextULong()) same++;
            Assert.True(same < 3);
        }

        [Fact]
        public void DoublesAreInUnitInterval()
        {
            var r = new Rng(5);
            for (int i = 0; i < 10000; i++)
            {
                double d = r.NextDouble();
                Assert.InRange(d, 0.0, 0.9999999999999999);
            }
        }

        [Fact]
        public void ContinuesExactlyAfterSaveAndLoad()
        {
            var r = new Rng(1234);
            for (int i = 0; i < 10; i++) r.NextULong();
            string json = JsonSerializer.Serialize(r, SimJson.Save);
            var copy = JsonSerializer.Deserialize<Rng>(json, SimJson.Save);
            for (int i = 0; i < 50; i++) Assert.Equal(r.NextULong(), copy.NextULong());
        }

        [Fact]
        public void DistributionsHaveTheRightMeans()
        {
            var r = new Rng(7);
            const int n = 40000;
            double poisson = 0, gamma = 0, normal = 0, normalSq = 0;
            for (int i = 0; i < n; i++)
            {
                poisson += r.Poisson(3.0);
                gamma += r.Gamma(2.5, 0.4);
                double x = r.Normal();
                normal += x;
                normalSq += x * x;
            }
            Assert.InRange(poisson / n, 2.95, 3.05);
            Assert.InRange(gamma / n, 0.98, 1.02);
            Assert.InRange(normal / n, -0.02, 0.02);
            Assert.InRange(Math.Sqrt(normalSq / n), 0.98, 1.02);
        }

        [Fact]
        public void LargePoissonUsesNormalApproximation()
        {
            var r = new Rng(11);
            double sum = 0;
            for (int i = 0; i < 20000; i++) sum += r.Poisson(200);
            Assert.InRange(sum / 20000, 199, 201);
        }
    }
}
