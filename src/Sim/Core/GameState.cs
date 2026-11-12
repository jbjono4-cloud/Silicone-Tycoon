using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SiliconeTycoon.Sim
{
    public class NewsItem
    {
        public int Tick;
        public string Text;
        /// <summary>0 info, 1 good, 2 warning, 3 bad.</summary>
        public int Severity;
    }

    /// <summary>Everything that changes during a game. Saved and loaded whole as JSON.</summary>
    public class GameState
    {
        public int SaveVersion = SaveGame.Version;
        public string CompanyName = "Your Company";
        public string ScenarioId = "campaign";
        public int Tick;
        public int StartTick;
        public ulong Seed;
        public Rng Rng;
        public int NextId = 1;

        /// <summary>Reputation from -1 (notorious) through 0 (unknown newcomer) to 1 (industry legend). Shown as -100 to 100.</summary>
        public double Rep;

        public Ledger Ledger = new Ledger();
        public int OverdrawnMonths;
        public bool GameOver;
        public string EndText;

        public List<string> NodesReady = new List<string>();
        public List<NodeProgram> NodePrograms = new List<NodeProgram>();
        public List<Fab> Fabs = new List<Fab>();
        public List<NewsItem> News = new List<NewsItem>();

        [JsonIgnore] public double Cash => Ledger.Cash;
        [JsonIgnore] public int Year => Calendar.YearOf(Tick);
        [JsonIgnore] public double YearF => Calendar.YearF(Tick);
        [JsonIgnore] public int MonthIndex => Calendar.MonthIndex(Tick);
        [JsonIgnore] public double Inflation => Calendar.Inflation(YearF);

        /// <summary>How reputation affects buyers and lenders: 0.25 at -100, 0.65 for a newcomer, 1.25 for a legend. (From Console Tycoon.)</summary>
        [JsonIgnore] public double RepFactor => Rep >= 0 ? 0.65 + 0.6 * Rep : 0.65 + 0.4 * Rep;

        public static string RepWord(double rep) => rep >= 0.75 ? "industry legend" : rep >= 0.45 ? "well respected" : rep >= 0.15 ? "known in the trade" : rep > -0.15 ? "newcomer" : rep > -0.5 ? "distrusted" : "notorious";

        public string NewId(string prefix) => prefix + (NextId++);
        public bool NodeReady(string nodeId) => NodesReady.Contains(nodeId);

        public void Log(string text, int severity = 0)
        {
            News.Add(new NewsItem { Tick = Tick, Text = text, Severity = severity });
            if (News.Count > 500) News.RemoveAt(0);
        }
    }
}
