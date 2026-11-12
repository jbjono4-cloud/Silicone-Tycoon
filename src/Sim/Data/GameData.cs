using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// A manufacturing process. Money values are in 1970 dollars; the sim multiplies them by
    /// <see cref="Calendar.Inflation"/> when it charges them.
    /// </summary>
    public class ProcessNodeDef
    {
        public string Id;
        public string Name;
        /// <summary>Feature size in nanometres.</summary>
        public double FeatureNm;
        /// <summary>Year the node first reaches production in history.</summary>
        public int Year;
        /// <summary>Previous node that must be ready before research can start (null for the first node).</summary>
        public string Requires;

        /// <summary>Logic transistors per mm².</summary>
        public double LogicDensity;
        /// <summary>SRAM transistors per mm².</summary>
        public double SramDensity;
        public double MaxClockMHz;
        public double Voltage;

        public double WaferMm;
        /// <summary>Materials and consumables per wafer start.</summary>
        public double WaferCost;
        public double MaskSetCost;
        /// <summary>Weeks from wafer start to finished wafer.</summary>
        public int CycleWeeks;

        /// <summary>Defects per cm² when the node is new.</summary>
        public double D0Launch;
        /// <summary>Defects per cm² the node settles at when mature.</summary>
        public double D0Mature;
        /// <summary>Time constant of the learning curve, in months of normal-volume production.</summary>
        public double LearningMonths;

        /// <summary>Capital cost of a fab with 1,000 wafer starts per month.</summary>
        public double FabCostPer1000;
        public int BuildMonths;
        public int RampMonths;

        public double ResearchCost;
        public int ResearchMonths;
        /// <summary>Chance the research programme slips at the normal budget.</summary>
        public double SlipRisk;
    }

    /// <summary>A real-world place you can build in. Index values are relative to the United States (1.0); tuning values.</summary>
    public class LocationDef
    {
        public string Id;
        public string Name;
        public string Region;
        public int From = 1970;
        public int To = 9999;
        public double LabourIndex = 1;
        public double PowerIndex = 1;
        /// <summary>Share of fab capex paid by government grants.</summary>
        public double Subsidy;
        /// <summary>Annual chance of an earthquake disruption.</summary>
        public double QuakeRisk;
        /// <summary>Annual chance of a political or trade disruption.</summary>
        public double PoliticalRisk;
    }

    /// <summary>Static game data loaded from the JSON files in /data.</summary>
    public class GameData
    {
        public List<ProcessNodeDef> Nodes = new List<ProcessNodeDef>();
        public List<LocationDef> Locations = new List<LocationDef>();

        public ProcessNodeDef Node(string id) => Nodes.FirstOrDefault(n => n.Id == id);
        public LocationDef Location(string id) => Locations.FirstOrDefault(l => l.Id == id);

        public static GameData FromJson(string nodesJson, string locationsJson)
        {
            var d = new GameData
            {
                Nodes = JsonSerializer.Deserialize<List<ProcessNodeDef>>(nodesJson, SimJson.Data),
                Locations = JsonSerializer.Deserialize<List<LocationDef>>(locationsJson, SimJson.Data),
            };
            var problems = d.Validate();
            if (problems.Count > 0) throw new InvalidOperationException("Game data problems:\n" + string.Join("\n", problems));
            return d;
        }

        /// <summary>Loads nodes.json and locations.json from a folder on disk (tests and the headless runner).</summary>
        public static GameData FromFolder(string folder) => FromJson(
            System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "nodes.json")),
            System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "locations.json")));

        public List<string> Validate()
        {
            var p = new List<string>();
            foreach (var g in Nodes.GroupBy(n => n.Id).Where(g => g.Count() > 1)) p.Add($"Duplicate node id {g.Key}");
            foreach (var g in Locations.GroupBy(l => l.Id).Where(g => g.Count() > 1)) p.Add($"Duplicate location id {g.Key}");
            foreach (var n in Nodes)
            {
                if (n.Requires != null && Node(n.Requires) == null) p.Add($"{n.Id} requires unknown node {n.Requires}");
                if (n.D0Mature <= 0 || n.D0Mature >= n.D0Launch) p.Add($"{n.Id}: D0Mature must be above 0 and below D0Launch");
                if (n.LearningMonths <= 0) p.Add($"{n.Id}: LearningMonths must be positive");
                if (n.WaferMm <= 0 || n.LogicDensity <= 0) p.Add($"{n.Id}: wafer size and density must be positive");
                if (n.BuildMonths <= 0 || n.RampMonths <= 0 || n.CycleWeeks <= 0) p.Add($"{n.Id}: build, ramp and cycle times must be positive");
                if (n.Requires != null && n.ResearchMonths <= 0) p.Add($"{n.Id}: a researched node needs ResearchMonths");
            }
            foreach (var l in Locations)
                if (l.LabourIndex <= 0 || l.PowerIndex <= 0 || l.Subsidy < 0 || l.Subsidy >= 1) p.Add($"{l.Id}: bad indexes");
            return p;
        }
    }
}
