using System;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class GameDataTests
    {
        static GameData D => TestData.Load();

        [Fact]
        public void DataLoadsAndValidates()
        {
            Assert.True(D.Nodes.Count >= 7);
            Assert.True(D.Locations.Count >= 10);
            Assert.Empty(D.Validate());
        }

        [Fact]
        public void EachNodeIsDenserAndLaterThanTheOneBefore()
        {
            foreach (var n in D.Nodes.Where(n => n.Requires != null))
            {
                var prev = D.Node(n.Requires);
                Assert.True(n.Year > prev.Year, n.Id);
                Assert.True(n.FeatureNm < prev.FeatureNm, n.Id);
                Assert.True(n.LogicDensity > prev.LogicDensity, n.Id);
                Assert.True(n.MaxClockMHz > prev.MaxClockMHz, n.Id);
            }
        }

        [Fact]
        public void ValidationCatchesBadNodes()
        {
            var bad = new GameData
            {
                Nodes =
                {
                    new ProcessNodeDef
                    {
                        Id = "x", Requires = "missing", D0Launch = 1, D0Mature = 2, LearningMonths = 1,
                        WaferMm = 100, LogicDensity = 1, BuildMonths = 1, RampMonths = 1, CycleWeeks = 1, ResearchMonths = 1,
                    },
                },
            };
            var problems = bad.Validate();
            Assert.Contains(problems, p => p.Contains("D0Mature"));
            Assert.Contains(problems, p => p.Contains("unknown node"));
        }

        [Fact]
        public void LoadingBadJsonThrows()
        {
            string nodes = "[{ \"Id\": \"a\", \"Requires\": \"nope\", \"D0Launch\": 5, \"D0Mature\": 1, \"LearningMonths\": 10, \"WaferMm\": 100, \"LogicDensity\": 10, \"BuildMonths\": 1, \"RampMonths\": 1, \"CycleWeeks\": 1, \"ResearchMonths\": 1 }]";
            Assert.Throws<InvalidOperationException>(() => GameData.FromJson(nodes, "[]"));
        }
    }
}
