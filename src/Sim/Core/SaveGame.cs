using System;
using System.Text.Json;

namespace SiliconeTycoon.Sim
{
    /// <summary>Saves are the whole <see cref="GameState"/> as JSON, versioned so old saves can be migrated.</summary>
    public static class SaveGame
    {
        public const int Version = 1;

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
            s.SaveVersion = Version;
        }
    }
}
