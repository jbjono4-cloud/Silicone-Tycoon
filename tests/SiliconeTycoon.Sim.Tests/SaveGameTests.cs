using System;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class SaveGameTests
    {
        static GameData D => TestData.Load();

        [Fact]
        public void LoadedGameContinuesIdentically()
        {
            var a = TestData.NewGame(seed: 77);
            Assert.Null(FabOps.Build(D, a, null, "n10um", "us_ca", 1000));
            Assert.Null(ProcessResearch.Start(D, a, "n8um", 0.7));
            Simulation.AdvanceMonths(D, a, 30);

            var b = SaveGame.Deserialize(SaveGame.Serialize(a));
            Simulation.AdvanceMonths(D, a, 30);
            Simulation.AdvanceMonths(D, b, 30);

            Assert.Equal(SaveGame.Serialize(a), SaveGame.Serialize(b));
        }

        [Fact]
        public void SameSeedSameGame()
        {
            string Run()
            {
                var s = TestData.NewGame(seed: 12);
                FabOps.Build(D, s, null, "n10um", "jp", 600);
                ProcessResearch.Start(D, s, "n8um");
                Simulation.AdvanceMonths(D, s, 48);
                return SaveGame.Serialize(s);
            }
            Assert.Equal(Run(), Run());
        }

        [Fact]
        public void RejectsSavesFromNewerVersions()
        {
            var s = TestData.NewGame();
            s.SaveVersion = SaveGame.Version + 1;
            string json = SaveGame.Serialize(s);
            Assert.Throws<InvalidOperationException>(() => SaveGame.Deserialize(json));
        }
    }
}
