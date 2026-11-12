using System.Text.Json;

namespace SiliconeTycoon.Sim
{
    /// <summary>Shared JSON settings. The sim uses public fields, so IncludeFields is on (as in Console Tycoon's saves).</summary>
    public static class SimJson
    {
        /// <summary>For game data files: forgiving about case, comments and trailing commas.</summary>
        public static readonly JsonSerializerOptions Data = new JsonSerializerOptions
        {
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>For save files.</summary>
        public static readonly JsonSerializerOptions Save = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = false,
        };
    }
}
