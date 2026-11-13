using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SiliconeTycoon.Sim
{
    public class NewsItem
    {
        public int Tick;
        public string Text;
        /// <summary>0 info, 1 good, 2 warning, 3 bad.</summary>
        public int Severity;
        /// <summary>What happened, for the UI to react to (see <see cref="NewsKinds"/>). Null for general news.</summary>
        public string Kind;
    }

    /// <summary>News types the UI can react to, such as pausing the game.</summary>
    public static class NewsKinds
    {
        /// <summary>A customer opened a request for quotes.</summary>
        public const string Rfq = "rfq";
        /// <summary>A chip's silicon came back from validation and needs a decision (release or step).</summary>
        public const string Validated = "validated";
        /// <summary>A process node became ready.</summary>
        public const string NodeReady = "node";
        /// <summary>A world event happened.</summary>
        public const string Event = "event";
        /// <summary>An event needs the player's decision.</summary>
        public const string Decision = "decision";
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
        /// <summary>Best process learning (months) any of your fabs has reached on each node. Carries over to new fabs and research.</summary>
        public Dictionary<string, double> NodeExperience = new Dictionary<string, double>();
        public List<ChipDesign> Designs = new List<ChipDesign>();
        public List<DesignTeam> DesignTeams = new List<DesignTeam>();

        public List<StockItem> Inventory = new List<StockItem>();
        public List<Listing> Listings = new List<Listing>();
        public List<Rfq> Rfqs = new List<Rfq>();
        public List<Contract> Contracts = new List<Contract>();
        public List<CustomerState> Customers = new List<CustomerState>();
        public List<RivalState> Rivals = new List<RivalState>();
        public List<MarketRecord> MarketHistory = new List<MarketRecord>();
        public List<NewsItem> News = new List<NewsItem>();

        /// <summary>How often random events happen (0 = never, 1 = normal). Scripted history happens whenever it's above 0.</summary>
        public double EventRate = 1;
        public StartType StartType = StartType.Integrated;
        public List<ActiveEffect> Effects = new List<ActiveEffect>();
        public List<Decision> Decisions = new List<Decision>();
        /// <summary>Scripted events that have happened (or were history before the game began).</summary>
        public List<string> FiredEvents = new List<string>();

        public List<FoundryState> Foundries = new List<FoundryState>();
        public List<ClientState> Clients = new List<ClientState>();
        public List<WaferRfq> WaferRfqs = new List<WaferRfq>();
        public List<WaferContract> WaferContracts = new List<WaferContract>();
        public List<AsicDeal> AsicDeals = new List<AsicDeal>();

        [JsonIgnore] public double Cash => Ledger.Cash;
        [JsonIgnore] public int Year => Calendar.YearOf(Tick);
        [JsonIgnore] public double YearF => Calendar.YearF(Tick);
        [JsonIgnore] public int MonthIndex => Calendar.MonthIndex(Tick);
        [JsonIgnore] public double Inflation => Calendar.Inflation(YearF);

        /// <summary>How reputation affects buyers and lenders: 0.25 at -100, 0.65 for a newcomer, 1.25 for a legend. (From Console Tycoon.)</summary>
        [JsonIgnore] public double RepFactor => RepFactorOf(Rep);

        public static double RepFactorOf(double rep) => rep >= 0 ? 0.65 + 0.6 * rep : 0.65 + 0.4 * rep;

        public static string RepWord(double rep) => rep >= 0.75 ? "industry legend" : rep >= 0.45 ? "well respected" : rep >= 0.15 ? "known in the trade" : rep > -0.15 ? "newcomer" : rep > -0.5 ? "distrusted" : "notorious";

        public string NewId(string prefix) => prefix + (NextId++);
        public bool NodeReady(string nodeId) => NodesReady.Contains(nodeId);
        public ChipDesign Design(string id) => id == null ? null : Designs.FirstOrDefault(c => c.Id == id);

        public void Log(string text, int severity = 0, string kind = null)
        {
            News.Add(new NewsItem { Tick = Tick, Text = text, Severity = severity, Kind = kind });
            if (News.Count > 500) News.RemoveAt(0);
        }
    }
}
