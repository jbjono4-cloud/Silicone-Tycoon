using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SiliconeTycoon.Sim
{
    /// <summary>A block placed on the die. Positions and sizes are in mm; W and H are worked out for the design's node.</summary>
    public class PlacedBlock
    {
        public string Id;
        public string BlockId;
        public double X;
        public double Y;
        /// <summary>Width over height, 0.25 to 4. The block keeps its area; the player chooses its shape.</summary>
        public double Aspect = 1;
        public double W;
        public double H;

        public bool Contains(double x, double y) => x >= X && x < X + W && y >= Y && y < Y + H;
        public PlacedBlock Clone() => (PlacedBlock)MemberwiseClone();
    }

    /// <summary>One row of a bin table. A die goes into the first bin whose requirements it meets.</summary>
    public class BinDef
    {
        public string Name;
        public int MinCores;
        public int MinGraphics;
        public double MinClockMHz;

        public BinDef Clone() => (BinDef)MemberwiseClone();
    }

    public class DesignNote
    {
        /// <summary>0 info, 1 warning, 2 error (design can't proceed).</summary>
        public int Severity;
        public string Text;
    }

    /// <summary>Everything worked out from a floorplan.</summary>
    public class DesignStats
    {
        public bool Valid;
        public List<DesignNote> Notes = new List<DesignNote>();

        public double Transistors;
        public double LogicTransistors;
        public double SramTransistors;
        public double BlockAreaMm2;
        public double DieAreaMm2;
        public double Utilisation;

        public int Cores;
        public int GraphicsUnits;
        public int RamBytes;
        public int RomBytes;
        public int PadsSupplied;
        public int PinsNeeded;

        /// <summary>Clock a typical die reaches.</summary>
        public double MaxClockMHz;
        /// <summary>Rated clock the chip is sold at.</summary>
        public double ClockMHz;
        public double LongestWireMm;
        public double PowerW;

        public double Compute;
        public double Graphics;
        public double Control;

        public int DiesPerWafer;
        public double YieldEstimate;
        public double CostPerGoodDie;

        public double EngineerMonths;
        public double MaskCost;
        public double DesignHouseFee;

        public void Add(int severity, string text) => Notes.Add(new DesignNote { Severity = severity, Text = text });
        [JsonIgnore] public string FirstError => Notes.FirstOrDefault(n => n.Severity >= 2)?.Text;
    }

    public enum DesignStage { Draft, Designing, Silicon, Validated, Released, Cancelled }

    /// <summary>A chip: a floorplan of blocks on one process node, plus its bin table and development state.</summary>
    public class ChipDesign
    {
        public string Id;
        public string Name;
        public string NodeId;
        public string PackageId;
        public double DieW = 4;
        public double DieH = 4;
        public List<PlacedBlock> Blocks = new List<PlacedBlock>();
        /// <summary>Rated clock. 0 = 90% of what a typical die reaches.</summary>
        public double TargetClockMHz;
        /// <summary>Share of engineering time spent on verification, 0.1 to 0.6. More finds bugs before tape-out.</summary>
        public double Verification = 0.3;
        /// <summary>Layout done by an outside design house: faster, but costs a fee and gives a slightly slower chip.</summary>
        public bool UseDesignHouse;
        public List<BinDef> Bins = new List<BinDef>();

        public DesignStage Stage = DesignStage.Draft;
        public string TeamId;
        public int WeeksLeft;
        public int StartedTick = -1;
        public int ReleasedTick = -1;
        /// <summary>0 = first silicon (A0), 1 = A1 and so on.</summary>
        public int Stepping;
        /// <summary>Hidden: bugs in the silicon nobody has found yet.</summary>
        public int HiddenBugs;
        /// <summary>Bugs found in validation and not yet fixed.</summary>
        public int FoundBugs;
        /// <summary>Found bugs bad enough that the chip can't ship.</summary>
        public int Showstoppers;
        /// <summary>Bugs that shipped unknown (errata waiting to be discovered by customers).</summary>
        public int EscapedBugs;
        public double MaskSpend;

        public DesignStats Stats;

        [JsonIgnore] public string SteppingName => "A" + Stepping;
        [JsonIgnore] public double DieAreaMm2 => DieW * DieH;

        public ChipDesign Clone()
        {
            var c = (ChipDesign)MemberwiseClone();
            c.Blocks = Blocks.Select(b => b.Clone()).ToList();
            c.Bins = Bins.Select(b => b.Clone()).ToList();
            return c;
        }
    }
}
