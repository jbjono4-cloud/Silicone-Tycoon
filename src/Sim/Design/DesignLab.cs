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

        /// <summary>Clock lost per mm of (weighted) wire between a core and the blocks it talks to.</summary>
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

        // pipelining: each extra stage shortens the critical path, loses some work per clock and adds latches
        public const double PipelineClockGain = 0.4;
        public const double PipelineIpcLoss = 0.12;
        public const double PipelineLogic = 0.15;
        // cell libraries
        public const double FastDepth = 0.8, FastArea = 1.3, FastActivity = 1.5;
        public const double DenseDepth = 1.2, DenseArea = 0.85, DenseActivity = 0.75;
        // routing congestion: above this utilisation the top-level wiring runs out of room
        public const double CongestionStart = 0.75;
        public const double CongestionLossPerUnit = 1.0;
        public const double MaxUtilisation = 0.95;
        // heat: a block's neighbours add part of their heat, falling off with distance
        public const double HeatSpread = 0.3;
        public const double HeatSpreadMm = 0.5;
        /// <summary>Bond pad pitch at 10 µm, mm; shrinks with the square root of the feature size.</summary>
        public const double PadPitchAt10um = 0.3;
        /// <summary>Timing closure: clock gained per unit of effort, and the most effort allowed.</summary>
        public const double TimingGain = 0.5;
        public const double MaxTimingEffort = 0.3;

        public static double FeatureScale(ProcessNodeDef n) => n.FeatureNm / 10000.0;

        public static double PadPitch(ProcessNodeDef n) => PadPitchAt10um * Math.Sqrt(FeatureScale(n));

        /// <summary>A block's numbers after the design's choices (pipeline stages, size, cell library).</summary>
        public struct EffBlock
        {
            public double Logic, Sram, Fixed, Activity, Depth, Ipc, Graphics, BytesPerClock;
            public double ComputeBoost, ControlBoost, BandwidthBoost;
            public int Pins, RamBytes, RomBytes, Repair;
            public double AreaFactor;
            public double Transistors => Logic + Sram;
        }

        public static EffBlock Eff(BlockDef b, PlacedBlock p = null)
        {
            var e = new EffBlock
            {
                Logic = b.LogicTransistors, Sram = b.SramTransistors, Fixed = b.FixedAreaMm2, Activity = b.Activity, Depth = b.LogicDepth,
                Ipc = b.Ipc, Graphics = b.Graphics, BytesPerClock = b.BytesPerClock, ComputeBoost = b.ComputeBoost, ControlBoost = b.ControlBoost,
                BandwidthBoost = b.BandwidthBoost, Pins = b.Pins, RamBytes = b.RamBytes, RomBytes = b.RomBytes, Repair = b.RepairCapacity, AreaFactor = 1,
            };
            if (p == null) return e;
            int k = Math.Clamp(p.Pipeline, 1, Math.Max(1, b.MaxPipeline));
            if (b.Kind == BlockKind.Core && k > 1)
            {
                e.Depth /= 1 + PipelineClockGain * (k - 1);
                e.Ipc /= 1 + PipelineIpcLoss * (k - 1);
                e.Logic *= 1 + PipelineLogic * (k - 1);
            }
            int sc = Math.Clamp(p.Scale, 1, Math.Max(1, b.MaxScale));
            if (sc > 1)
            {
                if (b.Kind == BlockKind.Ram || b.Kind == BlockKind.Rom)
                {
                    e.Logic *= 1 + 0.5 * (sc - 1);         // decoders and sense amps grow slower than the array
                    e.Sram *= sc;
                    e.RamBytes *= sc;
                    e.RomBytes *= sc;
                    e.Repair *= sc;
                    e.Depth += 0.03 * (sc - 1);           // longer bit lines
                }
                else if (b.Kind == BlockKind.MemoryInterface)
                {
                    e.Logic *= sc;
                    e.Fixed *= sc;
                    e.Pins *= sc;
                    e.BytesPerClock *= sc;
                }
            }
            switch (p.Cells)
            {
                case CellLibrary.Fast: e.Depth *= FastDepth; e.AreaFactor = FastArea; e.Activity *= FastActivity; break;
                case CellLibrary.Dense: e.Depth *= DenseDepth; e.AreaFactor = DenseArea; e.Activity *= DenseActivity; break;
            }
            return e;
        }

        public static double BlockArea(EffBlock e, ProcessNodeDef n) =>
            e.Logic / n.LogicDensity * e.AreaFactor + e.Sram / n.SramDensity + e.Fixed * Math.Pow(FeatureScale(n), 0.35);

        public static double BlockArea(BlockDef b, ProcessNodeDef n) => BlockArea(Eff(b), n);

        /// <summary>Sets a placed block's width and height from its area and aspect ratio.</summary>
        public static void Shape(PlacedBlock p, double area)
        {
            double aspect = Math.Clamp(p.Aspect, 0.25, 4);
            p.Aspect = aspect;
            p.W = Math.Sqrt(area * aspect);
            p.H = area / p.W;
        }

        /// <summary>Sizes every block for the design's node and settings. Call after changing the node, a block or its settings.</summary>
        public static void Layout(GameData d, ChipDesign c)
        {
            var n = d.Node(c.NodeId);
            if (n == null) return;
            foreach (var p in c.Blocks)
            {
                var b = d.Block(p.BlockId);
                if (b != null) Shape(p, BlockArea(Eff(b, p), n));
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

        /// <summary>How much a wire from a core to a block of this kind slows the clock, per mm.</summary>
        public static double WireWeight(BlockDef b)
        {
            if (b.WireWeight > 0) return b.WireWeight;
            return b.Kind switch
            {
                BlockKind.Ram => 1.0,
                BlockKind.Rom => 0.8,
                BlockKind.MemoryInterface => 0.8,
                BlockKind.Graphics => 0.5,
                BlockKind.Core => 0.5,
                BlockKind.Peripheral => 0.3,
                _ => 0,
            };
        }

        /// <summary>
        /// The links that carry fast signals: every core to every block it works with (and to other cores), each graphics
        /// unit to its nearest memory interface, and each memory interface to its nearest I/O ring. Weight is how much a mm
        /// of that wire costs in clock speed relative to a core-to-RAM wire.
        /// </summary>
        public static List<(PlacedBlock a, PlacedBlock b, double weight)> Connections(GameData d, ChipDesign c)
        {
            var list = new List<(PlacedBlock, PlacedBlock, double)>();
            var defs = c.Blocks.Select(p => (p, b: d.Block(p.BlockId))).Where(x => x.b != null).ToList();
            var cores = defs.Where(x => x.b.Kind == BlockKind.Core).ToList();
            for (int i = 0; i < cores.Count; i++)
            {
                foreach (var o in defs.Where(x => x.b.Kind != BlockKind.Core && x.b.Kind != BlockKind.Io))
                    list.Add((cores[i].p, o.p, WireWeight(o.b)));
                for (int j = i + 1; j < cores.Count; j++) list.Add((cores[i].p, cores[j].p, WireWeight(cores[j].b)));
            }
            void Nearest(BlockKind from, BlockKind to, double w)
            {
                var targets = defs.Where(x => x.b.Kind == to).ToList();
                if (targets.Count == 0) return;
                foreach (var f in defs.Where(x => x.b.Kind == from))
                    list.Add((f.p, targets.OrderBy(t => Gap(f.p, t.p)).First().p, w));
            }
            Nearest(BlockKind.Graphics, BlockKind.MemoryInterface, 0.6);
            Nearest(BlockKind.MemoryInterface, BlockKind.Io, 0.4);
            return list;
        }

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
            var defs = new List<(PlacedBlock p, BlockDef b, EffBlock e)>();
            foreach (var p in c.Blocks)
            {
                var b = d.Block(p.BlockId);
                if (b == null) { st.Add(2, $"Unknown block {p.BlockId}."); continue; }
                if (s.Year < b.Year) st.Add(2, $"The {b.Name} isn't available until {b.Year}.");
                if (p.Pipeline < 1 || p.Pipeline > b.MaxPipeline)
                    st.Add(2, b.MaxPipeline > 1 ? $"The {b.Name} can be split into 1 to {b.MaxPipeline} pipeline stages." : $"The {b.Name} can't be pipelined.");
                if (p.Scale < 1 || p.Scale > b.MaxScale)
                    st.Add(2, b.MaxScale > 1 ? $"The {b.Name} can be made 1 to {b.MaxScale} times bigger." : $"The {b.Name} comes in one size.");
                defs.Add((p, b, Eff(b, p)));
            }
            if (defs.Count == 0) return st;

            // ---- transistors and area
            st.LogicTransistors = defs.Sum(x => x.e.Logic);
            st.SramTransistors = defs.Sum(x => x.e.Sram);
            st.Transistors = st.LogicTransistors + st.SramTransistors;
            st.BlockAreaMm2 = defs.Sum(x => x.p.W * x.p.H);
            st.DieAreaMm2 = c.DieAreaMm2;
            st.Utilisation = st.DieAreaMm2 > 0 ? st.BlockAreaMm2 / st.DieAreaMm2 : 0;
            if (c.DieW <= 0 || c.DieH <= 0) st.Add(2, "The die needs a width and height.");

            // ---- geometry
            foreach (var (p, b, _) in defs)
                if (!InsideDie(c, p)) st.Add(2, $"The {b.Name} sits off the edge of the die.");
            for (int i = 0; i < defs.Count; i++)
                for (int j = i + 1; j < defs.Count; j++)
                    if (Overlaps(defs[i].p, defs[j].p)) st.Add(2, $"The {defs[i].b.Name} overlaps the {defs[j].b.Name}.");
            foreach (var (p, b, _) in defs.Where(x => x.b.Kind == BlockKind.Io))
                if (!OnEdge(c, p)) st.Add(2, $"The {b.Name} must touch the die edge: bond wires can't reach the middle.");
            if (st.Utilisation < 0.6 && st.DieAreaMm2 > 0)
                st.Add(1, $"{1 - st.Utilisation:P0} of the die is empty. You pay for that silicon on every wafer.");

            // ---- function counts and features
            st.Cores = defs.Count(x => x.b.Kind == BlockKind.Core);
            st.GraphicsUnits = defs.Count(x => x.b.Kind == BlockKind.Graphics);
            st.RamBytes = defs.Sum(x => x.e.RamBytes);
            st.RomBytes = defs.Sum(x => x.e.RomBytes);
            st.Features = Features.Of(d, c).OrderBy(f => f).ToList();

            // ---- pins and pads
            st.PadsSupplied = defs.Where(x => x.b.Kind == BlockKind.Io).Sum(x => x.e.Pins);
            st.PinsNeeded = defs.Where(x => x.b.Kind != BlockKind.Io).Sum(x => x.e.Pins) + OverheadPins;
            st.PackagePins = pkg?.Pins ?? 0;
            if (st.PadsSupplied == 0) st.Add(2, "Add an I/O ring: a chip needs pads to talk to the outside world.");
            else if (st.PadsSupplied < st.PinsNeeded) st.Add(2, $"The chip needs {st.PinsNeeded} pins but the I/O ring has {st.PadsSupplied} pads.");
            if (pkg != null && st.PadsSupplied > pkg.Pins) st.Add(2, $"The {pkg.Name} has {pkg.Pins} pins; the I/O ring has {st.PadsSupplied} pads.");
            if (pkg != null && st.DieAreaMm2 > pkg.MaxDieMm2) st.Add(2, $"A {st.DieAreaMm2:0.0} mm² die won't fit the {pkg.Name} (max {pkg.MaxDieMm2:0} mm²).");
            st.PadPerimeterMm = st.PadsSupplied * PadPitch(n);
            double perimeter = 2 * (c.DieW + c.DieH);
            if (st.PadsSupplied > 0 && perimeter < st.PadPerimeterMm - Eps)
                st.Add(2, $"Pad-limited: {st.PadsSupplied} bond pads need {st.PadPerimeterMm:0.0} mm of die edge, but the die's edge is {perimeter:0.0} mm. Make the die bigger or use fewer pads.");

            // ---- clock: slowest block, the worst weighted wire, congestion, layout and timing effort
            double blockLimit = defs.Min(x => n.MaxClockMHz / x.e.Depth);
            double longest = 0;
            string longestText = null;
            foreach (var (a, b, w) in Connections(d, c))
            {
                double g = Gap(a, b) * w;
                if (g > longest)
                {
                    longest = g;
                    longestText = $"{d.Block(a.BlockId).Name} to {d.Block(b.BlockId).Name}";
                }
            }
            st.LongestWireMm = longest;
            st.LongestWire = longestText;
            if (st.Utilisation > MaxUtilisation)
                st.Add(2, $"At {st.Utilisation:P0} of the die the blocks are too tightly packed to route the wiring between them (the limit is {MaxUtilisation:P0}).");
            st.CongestionLoss = st.Utilisation > CongestionStart ? Math.Min(0.5, CongestionLossPerUnit * (st.Utilisation - CongestionStart)) : 0;
            if (st.CongestionLoss > 0.005 && st.Utilisation <= MaxUtilisation)
                st.Add(1, $"The die is {st.Utilisation:P0} full: wiring detours around the blocks cost {st.CongestionLoss:P0} of clock speed. Leave some room for routing.");
            double effort = c.TimingEffort;
            if (effort < -Eps || effort > MaxTimingEffort + Eps) st.Add(2, $"Timing closure effort must be between 0% and {MaxTimingEffort:P0}.");
            effort = Math.Clamp(effort, 0, MaxTimingEffort);
            st.MaxClockMHz = blockLimit / (1 + WireDelayPerMm * longest) * (1 - st.CongestionLoss)
                             * (c.UseDesignHouse ? DesignHouseClock : 1) * (1 + TimingGain * effort);
            st.ClockMHz = c.TargetClockMHz > 0 ? c.TargetClockMHz : Math.Round(st.MaxClockMHz * 0.9, 2);
            if (longest > 0.5) st.Add(1, $"The {longestText} wire is long ({longest:0.0} mm, weighted by how busy it is): it costs {1 - 1 / (1 + WireDelayPerMm * longest):P0} of clock speed.");
            if (st.ClockMHz > st.MaxClockMHz) st.Add(1, $"The rated {st.ClockMHz:0.00} MHz is above the {st.MaxClockMHz:0.00} MHz a typical die reaches; most dies will fail that speed bin.");

            // ---- power: P = Σ(activity × transistors) × C × V² × f
            double perTransistorW = CapPerTransistor(n) * n.Voltage * n.Voltage * st.ClockMHz * 1e6;
            st.PowerW = defs.Sum(x => x.e.Activity * x.e.Transistors) * perTransistorW;
            if (pkg != null)
            {
                if (st.PowerW > pkg.MaxPowerW) st.Add(2, $"The chip draws {st.PowerW:0.00} W; the {pkg.Name} can shed {pkg.MaxPowerW:0.0} W.");
                else if (st.PowerW > pkg.MaxPowerW * 0.85) st.Add(1, $"{st.PowerW:0.00} W is close to the {pkg.Name}'s {pkg.MaxPowerW:0.0} W limit.");
            }

            // ---- hot spots: each block's own heat per mm² plus part of its neighbours'
            var dens = defs.Select(x => x.p.W * x.p.H > 0 ? x.e.Activity * x.e.Transistors * perTransistorW / (x.p.W * x.p.H) : 0).ToArray();
            int hottest = -1;
            for (int i = 0; i < defs.Count; i++)
            {
                double h = dens[i];
                for (int j = 0; j < defs.Count; j++)
                    if (j != i) h += HeatSpread * dens[j] * Math.Exp(-Gap(defs[i].p, defs[j].p) / HeatSpreadMm);
                st.BlockHeat[defs[i].p.Id ?? i.ToString()] = h;
                if (h > st.PeakHeat) { st.PeakHeat = h; hottest = i; }
            }
            st.HeatLimit = pkg?.MaxWPerMm2 ?? double.PositiveInfinity;
            st.ThermalClockMHz = st.PeakHeat > 0 ? st.ClockMHz * st.HeatLimit / st.PeakHeat : double.PositiveInfinity;
            if (pkg != null && hottest >= 0)
            {
                string where = defs[hottest].b.Name;
                if (st.PeakHeat > st.HeatLimit)
                    st.Add(2, $"Hot spot: the {where} reaches {st.PeakHeat:0.000} W/mm², more than the {pkg.Name} can draw away ({st.HeatLimit:0.00}). " +
                              $"Rate it at {Math.Floor(st.ThermalClockMHz * 100) / 100:0.00} MHz or less, move hot blocks apart, use dense cells, or pick a package that cools better.");
                else if (st.PeakHeat > st.HeatLimit * 0.85)
                    st.Add(1, $"The {where} runs hot ({st.PeakHeat:0.000} of {st.HeatLimit:0.00} W/mm² the package can take).");
            }

            // ---- performance
            double clock = st.ClockMHz;
            double ipc = defs.Where(x => x.b.Kind == BlockKind.Core).Sum(x => x.e.Ipc);
            double bwBoost = defs.Sum(x => x.e.BandwidthBoost);
            double bandwidth = defs.Where(x => x.b.Kind == BlockKind.MemoryInterface).Sum(x => x.e.BytesPerClock) * clock * (1 + bwBoost);
            double need = ipc * clock * 0.6;
            double memFactor = need <= 0 ? 0 : bandwidth <= 0 ? 0.4 : Math.Min(1.0, 0.4 + 0.6 * bandwidth / need);
            double ramFactor = 1 + 0.1 * Math.Log2(1 + st.RamBytes / 16.0);
            st.ComputeBoost = defs.Sum(x => x.e.ComputeBoost);
            foreach (var x in defs.Where(x => x.b.Disableable && x.e.ComputeBoost > 0 && x.b.Kind != BlockKind.Core))
                foreach (var f in x.b.Features)
                    st.FeatureComputeBoost[f] = (st.FeatureComputeBoost.TryGetValue(f, out var v) ? v : 0) + x.e.ComputeBoost;
            st.Compute = ipc * clock * memFactor * ramFactor * (1 + st.ComputeBoost);
            double gfx = defs.Where(x => x.b.Kind == BlockKind.Graphics).Sum(x => x.e.Graphics);
            st.Graphics = gfx * clock * (bandwidth > 0 ? 1.0 : 0.5);
            st.Control = ipc * clock * (1 + 0.25 * Math.Log2(1 + st.RomBytes / 256.0)) * (1 + defs.Sum(x => x.e.ControlBoost));
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
            foreach (var bin in c.Bins.Where(b => b.Features != null))
                foreach (var f in bin.Features.Where(f => !st.Features.Contains(f)))
                    st.Add(2, $"The {bin.Name} bin asks for {Features.Name(f)}, which the chip doesn't have.");

            st.Valid = st.Notes.All(x => x.Severity < 2);
            return st;
        }

        /// <summary>
        /// The outside design house's layout: square-ish blocks packed in shelves with generous spacing, I/O along the
        /// bottom edge, the core next, then the blocks with the busiest links to it. Die sized to fit. Valid but not
        /// tight. Marks the design as using the design house.
        /// </summary>
        public static void AutoPlace(GameData d, ChipDesign c, double spacingMm = 0.25)
        {
            foreach (var p in c.Blocks) p.Aspect = 1;
            Layout(d, c);
            var ordered = c.Blocks
                .OrderByDescending(p => d.Block(p.BlockId)?.Kind == BlockKind.Io)
                .ThenByDescending(p => d.Block(p.BlockId)?.Kind == BlockKind.Core)
                .ThenByDescending(p => d.Block(p.BlockId) is BlockDef b && b.Kind != BlockKind.Io && b.Kind != BlockKind.Core ? WireWeight(b) : 0)
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
            // pad-limited: grow the die until the edge holds every pad
            var n = d.Node(c.NodeId);
            int pads = c.Blocks.Select(p => d.Block(p.BlockId)).Where(b => b?.Kind == BlockKind.Io).Sum(b => b.Pins);
            if (n != null && pads > 0)
            {
                double need = pads * PadPitch(n);
                while (2 * (c.DieW + c.DieH) < need) { c.DieW = Math.Round(c.DieW + 0.1, 2); c.DieH = Math.Round(c.DieH + 0.1, 2); }
            }
            c.UseDesignHouse = true;
        }

        /// <summary>
        /// A straightforward chip for a segment on a node, from blocks available this year: an efficient core plus ROM for
        /// control work; the fastest core plus RAM no bigger than it for computing; an efficient core plus the best graphics
        /// unit for consoles; plus blocks for the features buyers want. Null if nothing fits.
        /// </summary>
        public static ChipDesign PlanForSegment(GameData d, GameState s, string nodeId, Metric metric, string name,
            bool ignoreNodeReady = false, double maxPowerW = double.PositiveInfinity) =>
            PlanForSegment(d, s, nodeId, metric, name, out _, ignoreNodeReady, maxPowerW);

        public static ChipDesign PlanForSegment(GameData d, GameState s, string nodeId, Metric metric, string name, out string why,
            bool ignoreNodeReady = false, double maxPowerW = double.PositiveInfinity)
        {
            var seg = d.Segments.FirstOrDefault(x => x.Metric == metric && x.Active(s.YearF)) ?? d.Segments.FirstOrDefault(x => x.Metric == metric);
            var spec = seg != null ? ChipSpec.ForSegment(seg, s.YearF) : new ChipSpec { Metric = metric };
            spec.MaxPowerW = maxPowerW;
            return PlanForSpec(d, s, nodeId, spec, name, out why, ignoreNodeReady);
        }

        /// <summary>
        /// The design house's answer to a brief. It starts from the strongest blocks for the job with every wanted feature
        /// and steps down (a smaller core, less memory, a narrower bus, then only the must-have features) until the chip
        /// fits a package, the power and heat limits and the brief. Says why when nothing fits.
        /// </summary>
        public static ChipDesign PlanForSpec(GameData d, GameState s, string nodeId, ChipSpec spec, string name, out string why, bool ignoreNodeReady = false)
        {
            why = null;
            int year = s.Year;
            var metric = spec.Metric;
            List<BlockDef> Avail(BlockKind k) => d.Blocks.Where(b => b.Kind == k && b.Year <= year).ToList();

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
                ? Avail(BlockKind.MemoryInterface).OrderBy(b => b.Transistors).ToList()
                : Avail(BlockKind.MemoryInterface).OrderByDescending(b => b.BytesPerClock).ToList();
            var ios = Avail(BlockKind.Io).OrderBy(b => b.Pins).ToList();
            var pkgs = d.Packages.Where(p => p.Year <= year && p.Pins <= spec.MaxPins).OrderBy(p => p.Cost).ToList();
            var featureSets = new List<List<string>> { spec.Features.Union(spec.Wanted).Distinct().ToList() };
            if (spec.Wanted.Except(spec.Features).Any()) featureSets.Add(spec.Features.Distinct().ToList());

            // cheapest block available this year for a feature
            BlockDef For(string feature) => d.Blocks.Where(b => b.Year <= year && b.Features.Contains(feature) && b.Kind != BlockKind.Core)
                                                     .OrderBy(b => b.Transistors).FirstOrDefault();

            var coreChoices = cores.Count > 0 ? cores.Cast<BlockDef>().ToList() : new List<BlockDef> { null };
            foreach (var feats in featureSets)
                    foreach (var core in coreChoices)
                    {
                        if (core != null && feats.Any(f => IsCoreFeature(d, f)) && !feats.Where(f => IsCoreFeature(d, f)).All(core.Features.Contains)) continue;
                        var extraChoices = Extras(core).Cast<BlockDef>().Append(null).ToList();
                        if (metric == Metric.Graphics) extraChoices.RemoveAt(extraChoices.Count - 1);   // a console chip needs graphics
                        foreach (var extra in extraChoices)
                            foreach (var mem in mems.Cast<BlockDef>().Append(null))
                            {
                                var picked = new List<(BlockDef b, int scale)>();
                                if (core != null) picked.Add((core, 1));
                                if (extra != null)
                                {
                                    int scale = 1;
                                    if (extra.RamBytes > 0 && spec.MinRamBytes > 0) scale = (int)Math.Ceiling(spec.MinRamBytes / (double)extra.RamBytes);
                                    if (extra.RomBytes > 0 && spec.MinRomBytes > 0) scale = (int)Math.Ceiling(spec.MinRomBytes / (double)extra.RomBytes);
                                    if (scale > extra.MaxScale) continue;
                                    picked.Add((extra, Math.Max(1, scale)));
                                }
                                if (mem != null) picked.Add((mem, 1));
                                bool missing = false;
                                foreach (var f in feats)
                                {
                                    if (picked.Any(x => x.b.Features.Contains(f))) continue;
                                    var fb = For(f);
                                    if (fb == null) { missing = true; break; }
                                    picked.Add((fb, 1));
                                }
                                if (missing)
                                {
                                    why ??= $"Nothing available in {year} provides {Features.List(feats.Where(f => For(f) == null && !(core?.Features.Contains(f) ?? false)))}.";
                                    continue;
                                }
                                int pinsNeeded = picked.Sum(x => Eff(x.b, new PlacedBlock { Scale = x.scale }).Pins) + OverheadPins;

                                foreach (var io in ios)
                                {
                                    if (io.Pins < pinsNeeded) continue;
                                    foreach (var pkg in pkgs)
                                    {
                                        if (pkg.Pins < io.Pins) continue;
                                        var c = new ChipDesign { Name = name, NodeId = nodeId, PackageId = pkg.Id };
                                        int i = 1;
                                        foreach (var (b, scale) in picked) c.Blocks.Add(new PlacedBlock { Id = "b" + i++, BlockId = b.Id, Scale = scale });
                                        c.Blocks.Add(new PlacedBlock { Id = "b" + i++, BlockId = io.Id });
                                        AutoPlace(d, c);
                                        var st = Analyze(d, s, c, ignoreNodeReady);
                                        double cap = st.ClockMHz;
                                        if (st.PowerW > spec.MaxPowerW && st.PowerW > 0) cap = Math.Min(cap, st.ClockMHz * spec.MaxPowerW / st.PowerW * 0.95);
                                        if (st.PeakHeat > st.HeatLimit) cap = Math.Min(cap, st.ThermalClockMHz * 0.98);
                                        if (cap < st.ClockMHz)
                                        {
                                            c.TargetClockMHz = Math.Floor(cap * 100) / 100;
                                            if (c.TargetClockMHz <= 0) continue;
                                            st = Analyze(d, s, c, ignoreNodeReady);
                                        }
                                        if (!st.Valid)
                                        {
                                            why ??= st.FirstError;
                                            continue;
                                        }
                                        c.Stats = st;
                                        var gaps = Specs.Check(d, spec, c, Binning.BinsFor(c)[0].Name, checkMetric: false);
                                        c.Stats = null;
                                        if (gaps.Count > 0) { why ??= gaps[0]; continue; }
                                        why = null;
                                        return c;
                                    }
                                }
                            }
                    }
            why ??= "No combination of this year's blocks and packages works.";
            return null;
        }

        /// <summary>Features only a core can provide (word length).</summary>
        static bool IsCoreFeature(GameData d, string f) => d.Blocks.Where(b => b.Features.Contains(f)).All(b => b.Kind == BlockKind.Core);

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
