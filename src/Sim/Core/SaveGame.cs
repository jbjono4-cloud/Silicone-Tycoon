using System;
using System.Text.Json;

namespace SiliconeTycoon.Sim
{
    /// <summary>Saves are the whole <see cref="GameState"/> as JSON, versioned so old saves can be migrated.</summary>
    public static class SaveGame
    {
        public const int Version = 3;

        public static string Serialize(GameState s) => JsonSerializer.Serialize(s, SimJson.Save);

        public static GameState Deserialize(string json)
        {
            var s = JsonSerializer.Deserialize<GameState>(json, SimJson.Save);
            if (s == null) throw new InvalidOperationException("The save file is empty.");
            if (s.SaveVersion > Version) throw new InvalidOperationException("This save was made by a newer version of the game.");
            Migrate(s);
            return s;
        }

        static void Migrate(GameState s)
        {
            if (s.Rng == null) s.Rng = new Rng(s.Seed);
            if (s.SaveVersion < 2)
            {
                // research programmes had a fixed hidden length; carry it over as a slip that has already happened (or not)
                foreach (var p in s.NodePrograms)
                {
                    p.Progress = p.WeeksDone;
                    p.FactorWeeks = p.WeeksDone * p.SpendFactor;
                    bool slipped = p.ActualWeeks > p.PlannedWeeks;
                    p.SlipRoll = slipped ? 0 : 1;
                    p.SlipSize = slipped && p.PlannedWeeks > 0 ? (p.ActualWeeks - p.PlannedWeeks) / (double)p.PlannedWeeks : 0;
                }
            }
            if (s.SaveVersion < 3)
            {
                // rivals used to produce on every node they had researched
                foreach (var r in s.Rivals)
                    if (r.ProductionNodes.Count == 0) r.ProductionNodes = new System.Collections.Generic.List<string>(r.NodesReady);
            }
            foreach (var f in s.Fabs)
                if (f.Producing && f.Learning > FabOps.Experience(s, f.NodeId)) s.NodeExperience[f.NodeId] = f.Learning;
            s.SaveVersion = Version;
        }
    }
}
