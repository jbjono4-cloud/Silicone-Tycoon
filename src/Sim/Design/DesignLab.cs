using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Works a floorplan into numbers: block sizes on the node, geometry checks, pins, clock, power, performance and cost.
    /// </summary>
    public static class DesignLab
    {
        const double Eps = 1e-6;

        /// <summary>Clock lost per mm of gap between a core and the blocks it talks to.</summary>
        public const double WireDelayPerMm = 0.12;
        /// <summary>Clock kept when an outside design house does the layout.</summary>
        public const double DesignHouseClock = 0.95;
        /// <summary>Switched capacitance per transistor at 10 µm (gate plus local wiring), in farads.</summary>
        public const double CapAt10um = 6e-12;
        public const double CapExponent = 1.3;
        /// <summary>Supply, ground, clock and reset pins every chip needs.</summary>
        public const int OverheadPins = 4;
        /// <summary>Months of fab learning assumed for the yield estimate shown while designing.</summary>
        public const double EstimateLearning = 12;

        public static double FeatureScale(ProcessNodeDef n) => n.FeatureNm / 10000.0;

        public static double BlockArea(BlockDef b, ProcessNodeDef n) =>
            b.LogicTransistors / n.LogicDensity + b.SramTransistors / n.SramDensity + b.FixedAreaMm2 * Math.Pow(FeatureScale(n), 0.35);

        /// <summary>Sets a placed block's width and height from its area and aspect ratio.</summary>
        public static void Shape(PlacedBlock p, double area)
        {
            double aspect = Math.Clamp(p.Aspect, 0.25, 4);
            p.Aspect = aspect;
            p.W = Math.Sqrt(area * aspect);
            p.H = area / p.W;
        }

        /// <summary>Sizes every block for the design's node. Call after changing the node, a block or an aspect ratio.</summary>
        public static void Layout(GameData d, ChipDesign c)
        {
            var n = d.Node(c.NodeId);
            if (n == null) return;
            foreach (var p in c.Blocks)
            {
                var b = d.Block(p.BlockId);
                if (b != null) Shape(p, BlockArea(b, n));
            }
        }

        public static bool Overlaps(PlacedBlock a, PlacedBlock b) =>
            a.X < b.X + b.W - Eps && b.X < a.X + a.W - Eps && a.Y < b.Y + b.H - Eps && b.Y < a.Y + a.H - Eps;

        public static bool InsideDie(ChipDesign c, PlacedBlock p) =>
            p.X >= -Eps && p.Y >= -Eps && p.X + p.W <= c.DieW + Eps && p.Y + p.H <= c.DieH + Eps;

        public static bool OnEdge(ChipDesign c, PlacedBlock p) =>
            p.X <= Eps || p.Y <= Eps || p.X + p.W >= c.DieW - Eps || p.Y + p.H >= c.DieH - Eps;

        /// <summary>Wiring distance between two blocks' nearest edges (Manhattan), 0 if they touch.</summary>
        public static double Gap(PlacedBlock a, PlacedBlock b)
        {
            double dx = Math.Max(0, Math.Max(a.X - (b.X + b.W), b.X - (a.X + a.W)));
            double dy = Math.Max(0, Math.Max(a.Y - (b.Y + b.H), b.Y - (a.Y + a.H)));
            return dx + dy;
        }

        /// <summary>Switched capacitance per transistor on a node.</summary>
        public static double CapPerTransistor(ProcessNodeDef n) => CapAt10um * Math.Pow(FeatureScale(n), CapExponent);

        /// <summary>Kinds a core needs short wires to.</summary>
        static readonly BlockKind[] CorePartners = { BlockKind.Ram, BlockKind.Rom, BlockKind.MemoryInterface, BlockKind.Graphics };

        /// <param name="ignoreNodeReady">For rivals, who track their own nodes.</param>
        public static DesignStats Analyze(GameData d, GameState s, ChipDesign c, bool ignoreNodeReady = false)
        {
            var st = new DesignStats();
            var n = d.Node(c.NodeId);
            var pkg = d.Package(c.PackageId);
            if (n == null) { st.Add(2, "Choose a process node."); return st; }
            if (!ignoreNodeReady && !s.NodeReady(n.Id)) st.Add(2, $"The {n.Name} process isn't ready yet.");
            if (pkg == null) st.Add(2, "Choose a package.");
            else if (s.Year < pkg.Year) st.Add(2, $"The {pkg.Name} isn't available until {pkg.Year}.");
            if (c.Blocks.Count == 0) { st.Add(2, "Place at least one block."); return st; }

            Layout(d, c);
            var defs = new List<(PlacedBlock p, BlockDef b)>();
            foreach (var p in c.Blocks)
            {
                var b = d.Block(p.BlockId);
                if (b == null) { st.Add(2, $"Unknown block {p.BlockId}."); continue; }
                if (s.Year < b.Year) st.Add(2, $"The {b.Name} isn't available until {b.Year}.");
                defs.Add((p, b));
            }
            if (defs.Count == 0) return st;

            // ---- transistors and area
            st.LogicTransistors = defs.Sum(x => x.b.LogicTransistors);
            st.SramTransistors = defs.Sum(x => x.b.SramTransistors);
            st.Transistors = st.LogicTransistors + st.SramTransistors;
            st.BlockAreaMm2 = defs.Sum(x => x.p.W * x.p.H);
            st.DieAreaMm2 = c.DieAreaMm2;
            st.Utilisation = st.DieAreaMm2 > 0 ? st.BlockAreaMm2 / st.DieAreaMm2 : 0;
            if (c.DieW <= 0 || c.DieH <= 0) st.Add(2, "The die needs a width and height.");

            // ---- geometry
            foreach (var (p, b) in defs)
                if (!InsideDie(c, p)) st.Add(2, $"The {b.Name} sits off the edge of the die.");
            for (int i = 0; i < defs.Count; i++)
                for (int j = i + 1; j < defs.Count; j++)
                    if (Overlaps(defs[i].p, defs[j].p)) st.Add(2, $"The {defs[i].b.Name} overlaps the {defs[j].b.Name}.");
            foreach (var (p, b) in defs.Where(x => x.b.Kind == BlockKind.Io))
                if (!OnEdge(c, p)) st.Add(2, $"The {b.Name} must touch the die edge: bond wires can't reach the middle.");
            if (st.Utilisation < 0.6 && st.DieAreaMm2 > 0)
                st.Add(1, $"{1 - st.Utilisation:P0} of the die is empty. You pay for that silicon on every wafer.");

            // ---- function counts
            st.Cores = defs.Count(x => x.b.Kind == BlockKind.Core);
            st.GraphicsUnits = defs.Count(x => x.b.Kind == BlockKind.Graphics);
            st.RamBytes = defs.Sum(x => x.b.RamBytes);
            st.RomBytes = defs.Sum(x => x.b.RomBytes);

            // ---- pins
            st.PadsSupplied = defs.Where(x => x.b.Kind == BlockKind.Io).Sum(x => x.b.Pins);
            st.PinsNeeded = defs.Where(x => x.b.Kind != BlockKind.Io).Sum(x => x.b.Pins) + OverheadPins;
            if (st.PadsSupplied == 0) st.Add(2, "Add an I/O ring: a chip needs pads to talk to the outside world.");
            else if (st.PadsSupplied < st.PinsNeeded) st.Add(2, $"The chip needs {st.PinsNeeded} pins but the I/O ring has {st.PadsSupplied} pads.");
            if (pkg != null && st.PadsSupplied > pkg.Pins) st.Add(2, $"The {pkg.Name} has {pkg.Pins} pins; the I/O ring has {st.PadsSupplied} pads.");
            if (pkg != null && st.DieAreaMm2 > pkg.MaxDieMm2) st.Add(2, $"A {st.DieAreaMm2:0.0} mm² die won't fit the {pkg.Name} (max {pkg.MaxDieMm2:0} mm²).");

            // ---- clock: slowest block, then wire delay between each core and its nearest partner of each kind
            double blockLimit = defs.Min(x => n.MaxClockMHz / x.b.LogicDepth);
            double longest = 0;
            foreach (var core in defs.Where(x => x.b.Kind == BlockKind.Core))
                foreach (var kind in CorePartners)
                {
                    var partners = defs.Where(x => x.b.Kind == kind).ToList();
                    if (partners.Count == 0) continue;
                    longest = Math.Max(longest, partners.Min(x => Gap(core.p, x.p)));
                }
            st.LongestWireMm = longest;
            st.MaxClockMHz = blockLimit / (1 + WireDelayPerMm * longest) * (c.UseDesignHouse ? DesignHouseClock : 1);
            st.ClockMHz = c.TargetClockMHz > 0 ? c.TargetClockMHz : Math.Round(st.MaxClockMHz * 0.9, 2);
            if (longest > 0.5) st.Add(1, $"A core is {longest:0.0} mm from a block it talks to; the wire costs {1 - 1 / (1 + WireDelayPerMm * longest):P0} of clock speed.");
            if (st.ClockMHz > st.MaxClockMHz) st.Add(1, $"The rated {st.ClockMHz:0.00} MHz is above the {st.MaxClockMHz:0.00} MHz a typical die reaches; most dies will fail that speed bin.");

            // ---- power: P = Σ(activity × transistors) × C × V² × f
            double switched = defs.Sum(x => x.b.Activity * x.b.Transistors);
            st.PowerW = switched * CapPerTransistor(n) * n.Voltage * n.Voltage * st.ClockMHz * 1e6;
            if (pkg != null)
            {
                if (st.PowerW > pkg.MaxPowerW) st.Add(2, $"The chip draws {st.PowerW:0.00} W; the {pkg.Name} can shed {pkg.MaxPowerW:0.0} W.");
                else if (st.PowerW > pkg.MaxPowerW * 0.85) st.Add(1, $"{st.PowerW:0.00} W is close to the {pkg.Name}'s {pkg.MaxPowerW:0.0} W limit.");
            }

            // ---- performance
            double clock = st.ClockMHz;
            double ipc = defs.Where(x => x.b.Kind == BlockKind.Core).Sum(x => x.b.Ipc);
            double bandwidth = defs.Where(x => x.b.Kind == BlockKind.MemoryInterface).Sum(x => x.b.BytesPerClock) * clock;
            double need = ipc * clock * 0.6;
            double memFactor = need <= 0 ? 0 : bandwidth <= 0 ? 0.4 : Math.Min(1.0, 0.4 + 0.6 * bandwidth / need);
            double ramFactor = 1 + 0.1 * Math.Log2(1 + st.RamBytes / 16.0);
            st.Compute = ipc * clock * memFactor * ramFactor;
            double gfx = defs.Where(x => x.b.Kind == BlockKind.Graphics).Sum(x => x.b.Graphics);
            st.Graphics = gfx * clock * (bandwidth > 0 ? 1.0 : 0.5);
            st.Control = ipc * clock * (1 + 0.25 * Math.Log2(1 + st.RomBytes / 256.0));
            if (st.Cores > 0 && bandwidth <= 0 && st.RomBytes == 0)
                st.Add(1, "The core has no memory interface and no ROM: it has nowhere to fetch a program from.");
            else if (memFactor < 0.9 && st.Cores > 0 && bandwidth > 0)
                st.Add(1, $"The memory interface can't keep the cores fed; they run at {memFactor:P0} of their potential.");

            // ---- manufacturing estimate
            double infl = s.Inflation;
            st.DiesPerWafer = YieldModel.DiesPerWafer(n.WaferMm, YieldModel.WithScribe(st.DieAreaMm2));
            st.YieldEstimate = YieldModel.Yield(LearningCurve.D0(n, EstimateLearning), st.DieAreaMm2);
            if (st.DiesPerWafer == 0) st.Add(2, "The die is too big for the wafer.");
            else
            {
                double good = st.DiesPerWafer * st.YieldEstimate * (pkg?.Yield ?? 1);
                st.CostPerGoodDie = good > 0 ? n.WaferCost * infl / good + (pkg?.Cost ?? 0) * infl : double.PositiveInfinity;
            }

            // ---- development cost
            st.EngineerMonths = DesignOps.EngineerMonths(c, st, s.YearF);
            st.MaskCost = n.MaskSetCost * infl;
            st.DesignHouseFee = c.UseDesignHouse
                ? DesignOps.BaseEngineerMonths(c, st, s.YearF) * DesignOps.DesignHouseShare * DesignOps.DesignHouseMarkup * DesignOps.Salary * infl
                : 0;

            if (c.Verification < DesignOps.MinVerification || c.Verification > DesignOps.MaxVerification)
                st.Add(2, $"Verification effort must be between {DesignOps.MinVerification:P0} and {DesignOps.MaxVerification:P0}.");
            foreach (var g in c.Bins.GroupBy(b => b.Name).Where(g => g.Count() > 1)) st.Add(2, $"Two bins are called {g.Key}.");
            if (c.Bins.Any(b => string.IsNullOrWhiteSpace(b.Name) || b.Name == Binning.Scrap)) st.Add(2, $"Every bin needs a name, and \"{Binning.Scrap}\" is reserved.");

            st.Valid = st.Notes.All(x => x.Severity < 2);
            return st;
        }

        /// <summary>
        /// The outside design house's layout: square-ish blocks packed in shelves with generous spacing, I/O along the
        /// bottom edge, die sized to fit. Valid but not tight. Marks the design as using the design house.
        /// </summary>
        public static void AutoPlace(GameData d, ChipDesign c, double spacingMm = 0.25)
        {
            foreach (var p in c.Blocks) p.Aspect = 1;
            Layout(d, c);
            var ordered = c.Blocks
                .OrderByDescending(p => d.Block(p.BlockId)?.Kind == BlockKind.Io)
                .ThenByDescending(p => p.W * p.H)
                .ToList();
            double total = c.Blocks.Sum(p => p.W * p.H);
            double rowWidth = Math.Max(Math.Sqrt(total * 1.4), c.Blocks.Count == 0 ? 1 : c.Blocks.Max(p => p.W));
            double x = 0, y = 0, rowH = 0, maxX = 0;
            foreach (var p in ordered)
            {
                if (x > 0 && x + p.W > rowWidth + Eps)
                {
                    x = 0;
                    y += rowH + spacingMm;
                    rowH = 0;
                }
                p.X = x;
                p.Y = y;
                x += p.W + spacingMm;
                rowH = Math.Max(rowH, p.H);
                maxX = Math.Max(maxX, p.X + p.W);
            }
            c.DieW = Math.Round(maxX + 0.005, 2);
            c.DieH = Math.Round(y + rowH + 0.005, 2);
            c.UseDesignHouse = true;
        }

        /// <summary>
        /// A straightforward chip for a segment on a node, from blocks available this year: an efficient core plus ROM for
        /// control work; the fastest core plus RAM no bigger than it for computing; an efficient core plus the best graphics
        /// unit for consoles. Uses the smallest I/O ring and cheapest package that pass analysis, and lowers the rated clock
        /// if needed to stay under <paramref name="maxPowerW"/>. Null if nothing fits.
        /// </summary>
        public static ChipDesign PlanForSegment(GameData d, GameState s, string nodeId, Metric metric, string name,
            bool ignoreNodeReady = false, double maxPowerW = double.PositiveInfinity) =>
            PlanForSegment(d, s, nodeId, metric, name, out _, ignoreNodeReady, maxPowerW);

        /// <summary>
        /// As above, but says why nothing fits. The design house starts from the strongest blocks for the market and steps
        /// down (a smaller core, less memory, a narrower bus) until the chip fits a package and the market's power limit.
        /// </summary>
        public static ChipDesign PlanForSegment(GameData d, GameState s, string nodeId, Metric metric, string name, out string why,
            bool ignoreNodeReady = false, double maxPowerW = double.PositiveInfinity)
        {
            why = null;
            int year = s.Year;
            List<BlockDef> Avail(BlockKind k) => d.Blocks.Where(b => b.Kind == k && b.Year <= year).ToList();

            // strongest first for the market
            var cores = metric == Metric.Compute
                ? Avail(BlockKind.Core).OrderByDescending(b => b.Ipc).ToList()
                : Avail(BlockKind.Core).OrderByDescending(b => b.Ipc / b.Transistors).ThenByDescending(b => b.Ipc).ToList();
            List<BlockDef> Extras(BlockDef core) => (metric switch
            {
                Metric.Control => Avail(BlockKind.Rom).OrderByDescending(b => b.RomBytes),
                Metric.Compute => Avail(BlockKind.Ram).Where(b => core == null || b.Transistors <= core.Transistors).OrderByDescending(b => b.RamBytes),
                _ => Avail(BlockKind.Graphics).OrderByDescending(b => b.Graphics),
            }).ToList();
            var mems = metric == Metric.Control
                ? Avail(BlockKind.MemoryInterface).OrderBy(b => b.Transistors).ToList()          // control code needs little bandwidth
                : Avail(BlockKind.MemoryInterface).OrderByDescending(b => b.BytesPerClock).ToList();
            var ios = Avail(BlockKind.Io).OrderBy(b => b.Pins).ToList();
            var pkgs = d.Packages.Where(p => p.Year <= year).OrderBy(p => p.Cost).ToList();

            var coreChoices = cores.Count > 0 ? cores.Cast<BlockDef>().ToList() : new List<BlockDef> { null };
            foreach (var core in coreChoices)
            {
                var extras = Extras(core);
                var extraChoices = extras.Cast<BlockDef>().Append(null).ToList();          // fall back to none
                if (metric == Metric.Graphics) extraChoices.RemoveAt(extraChoices.Count - 1); // a console chip needs graphics
                foreach (var extra in extraChoices)
                    foreach (var mem in mems.Cast<BlockDef>().Append(null))
                    {
                        var ids = new List<string>();
                        if (core != null) ids.Add(core.Id);
                        if (extra != null) ids.Add(extra.Id);
                        if (mem != null) ids.Add(mem.Id);
                        foreach (var io in ios)
                            foreach (var pkg in pkgs)
                            {
                                if (pkg.Pins < io.Pins) continue;
                                var c = Starter(d, name, nodeId, pkg.Id, ids.Concat(new[] { io.Id }).ToArray());
                                var st = Analyze(d, s, c, ignoreNodeReady);
                                if (st.Valid && st.PowerW > maxPowerW)
                                {
                                    c.TargetClockMHz = Math.Floor(st.ClockMHz * maxPowerW / st.PowerW * 0.95 * 100) / 100;
                                    if (c.TargetClockMHz <= 0) continue;
                                    st = Analyze(d, s, c, ignoreNodeReady);
                                }
                                if (st.Valid && st.PowerW <= maxPowerW) { why = null; return c; }
                                if (why == null && !st.Valid) why = st.Notes.FirstOrDefault(x => x.Severity >= 2)?.Text;
                            }
                    }
            }
            why ??= "No combination of this year's blocks and packages works.";
            return null;
        }

        // ---------------------------------------------------------------- hand layout helpers

        /// <summary>Adds a block to a floorplan at the first free spot inside the die (or just right of it if the die is full).</summary>
        public static PlacedBlock AddBlock(GameData d, ChipDesign c, string blockId)
        {
            var b = d.Block(blockId);
            var n = d.Node(c.NodeId);
            if (b == null || n == null) return null;
            int next = 1;
            while (c.Blocks.Any(x => x.Id == "b" + next)) next++;
            var p = new PlacedBlock { Id = "b" + next, BlockId = blockId, Aspect = 1 };
            Shape(p, BlockArea(b, n));
            PlaceFree(c, p);
            c.Blocks.Add(p);
            return p;
        }

        /// <summary>Moves a block to the first spot (scanning in 0.1 mm steps) where it fits inside the die without overlapping.</summary>
        public static void PlaceFree(ChipDesign c, PlacedBlock p, double step = 0.1)
        {
            var others = c.Blocks.Where(x => x != p).ToList();
            for (double y = 0; y + p.H <= c.DieH + Eps; y += step)
                for (double x = 0; x + p.W <= c.DieW + Eps; x += step)
                {
                    p.X = Math.Round(x, 2);
                    p.Y = Math.Round(y, 2);
                    if (!others.Any(o => Overlaps(o, p))) return;
                }
            p.X = Math.Round(Math.Max(c.DieW, others.Count == 0 ? 0 : others.Max(o => o.X + o.W)) + 0.3, 2);
            p.Y = 0;
        }

        /// <summary>Changes a block's shape, keeping its area and its centre.</summary>
        public static void SetAspect(PlacedBlock p, double aspect)
        {
            double cx = p.X + p.W / 2, cy = p.Y + p.H / 2;
            double area = p.W * p.H;
            p.Aspect = aspect;
            Shape(p, area);
            p.X = Math.Round(cx - p.W / 2, 3);
            p.Y = Math.Round(cy - p.H / 2, 3);
        }

        /// <summary>Shrinks (or grows) the die to the blocks' bounding box, moving everything so it starts at the corner.</summary>
        public static void FitDie(ChipDesign c)
        {
            if (c.Blocks.Count == 0) return;
            double minX = c.Blocks.Min(p => p.X), minY = c.Blocks.Min(p => p.Y);
            foreach (var p in c.Blocks)
            {
                p.X -= minX;
                p.Y -= minY;
            }
            c.DieW = Math.Ceiling(c.Blocks.Max(p => p.X + p.W) * 100 - 1e-6) / 100;
            c.DieH = Math.Ceiling(c.Blocks.Max(p => p.Y + p.H) * 100 - 1e-6) / 100;
        }

        /// <summary>
        /// A new draft from an existing chip's floorplan, optionally on another node (a die shrink: blocks keep their
        /// positions and get smaller, so the layout needs tightening). Development history is not copied.
        /// </summary>
        public static ChipDesign CopyAsNewChip(GameData d, ChipDesign src, string name, string nodeId = null)
        {
            var c = src.Clone();
            c.Id = null;
            c.Name = name;
            c.NodeId = nodeId ?? src.NodeId;
            c.Stage = DesignStage.Draft;
            c.TeamId = null;
            c.WeeksLeft = 0;
            c.StartedTick = -1;
            c.ReleasedTick = -1;
            c.Stepping = 0;
            c.HiddenBugs = 0;
            c.FoundBugs = 0;
            c.Showstoppers = 0;
            c.EscapedBugs = 0;
            c.MaskSpend = 0;
            c.Stats = null;
            Layout(d, c);
            return c;
        }

        /// <summary>A blank die to lay out by hand.</summary>
        public static ChipDesign Blank(GameData d, GameState s, string name, string nodeId)
        {
            var pkg = d.Packages.Where(p => p.Year <= s.Year).OrderBy(p => p.Cost).FirstOrDefault();
            return new ChipDesign { Name = name, NodeId = nodeId, PackageId = pkg?.Id, DieW = 4, DieH = 4, UseDesignHouse = false };
        }

        /// <summary>A starter design: the given blocks on a node and package, placed by the design house.</summary>
        public static ChipDesign Starter(GameData d, string name, string nodeId, string packageId, params string[] blockIds)
        {
            var c = new ChipDesign { Name = name, NodeId = nodeId, PackageId = packageId };
            int i = 1;
            foreach (var id in blockIds) c.Blocks.Add(new PlacedBlock { Id = "b" + i++, BlockId = id });
            AutoPlace(d, c);
            return c;
        }
    }
}
