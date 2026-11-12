using System;
using System.IO;

namespace SiliconeTycoon.Sim.Tests
{
    static class TestData
    {
        static GameData cached;

        /// <summary>The real game data, copied next to the test assembly by the .csproj.</summary>
        public static GameData Load() => cached ??= GameData.FromFolder(Path.Combine(AppContext.BaseDirectory, "data"));

        /// <summary>A new 1970 game with plenty of cash so tests aren't cut short by bankruptcy.</summary>
        public static GameState NewGame(ulong seed = 1, double cash = 20_000_000, int year = 1970) =>
            Simulation.NewGame(Load(), new NewGameOptions { Seed = seed, StartingCash = cash, StartYear = year });
    }
}
