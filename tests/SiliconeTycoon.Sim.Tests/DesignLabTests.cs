using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class DesignLabTests
    {
        static GameData D => TestData.Load();

        static ChipDesign Chip(string package, params string[] blocks) => DesignLab.Starter(D, "Test", "n10um", package, blocks);

        static PlacedBlock Find(ChipDesign c, string blockId) => c.Blocks.First(b => b.BlockId == blockId);

        [Fact]
        public void BlocksShrinkOnNewerNodes()
        {
            var core = D.Block("core4");
            var io = D.Block("io16");
            var n10 = D.Node("n10um");
            var n8 = D.Node("n8um");
            Assert.True(DesignLab.BlockArea(core, n8) < DesignLab.BlockArea(core, n10));
            double coreRatio = DesignLab.BlockArea(core, n8) / DesignLab.BlockArea(core, n10);
            double ioRatio = DesignLab.BlockArea(io, n8) / DesignLab.BlockArea(io, n10);
            Assert.True(ioRatio > coreRatio, "Pads should shrink more slowly than logic");
        }

        [Fact]
        public void AutoPlacedStarterIsValid()
        {
            var s = TestData.NewGame();
            var c = Chip("dip16p", "core4", "memif", "io16");
            var st = DesignLab.Analyze(D, s, c);
            Assert.True(st.Valid, st.FirstError);
            Assert.Equal(1, st.Cores);
            Assert.Equal(16, st.PadsSupplied);
            Assert.Equal(16, st.PinsNeeded);
            Assert.Equal(2300, st.Transistors, 6);
            Assert.InRange(st.Utilisation, 0.3, 1.0);
            Assert.True(st.CostPerGoodDie > 0);
        }

        [Fact]
        public void OverlappingBlocksAreAnError()
        {
            var s = TestData.NewGame();
            var c = Chip("dip16p", "core4", "memif", "io16");
            var mem = Find(c, "memif");
            var core = Find(c, "core4");
            mem.X = core.X;
            mem.Y = core.Y;
            var st = DesignLab.Analyze(D, s, c);
            Assert.False(st.Valid);
            Assert.Contains(st.Notes, n => n.Severity == 2 && n.Text.Contains("overlaps"));
        }

        [Fact]
        public void IoRingMustTouchTheEdge()
        {
            var s = TestData.NewGame();
            var c = Chip("dip40c", "core4", "memif", "io16");
            c.DieW += 4;
            c.DieH += 4;
            foreach (var b in c.Blocks) { b.X += 2; b.Y += 2; }
            var st = DesignLab.Analyze(D, s, c);
            Assert.Contains(st.Notes, n => n.Severity == 2 && n.Text.Contains("edge"));
        }

        [Fact]
        public void PinsMustAddUp()
        {
            var s = TestData.NewGame();
            var tooFewPads = Chip("dip40c", "core4", "memif", "memif", "io16");   // 12 + 12 + 4 needed, 16 pads
            Assert.Contains(DesignLab.Analyze(D, s, tooFewPads).Notes, n => n.Severity == 2 && n.Text.Contains("pads"));

            var tooFewPins = Chip("dip16p", "core4", "memif", "io40");            // 40 pads, 16-pin package
            Assert.Contains(DesignLab.Analyze(D, s, tooFewPins).Notes, n => n.Severity == 2 && n.Text.Contains("16 pins"));
        }

        [Fact]
        public void LongWiresCostClockSpeed()
        {
            var s = TestData.NewGame();
            var c = Chip("dip40c", "core4", "ram64", "memif", "io16");
            double near = DesignLab.Analyze(D, s, c).MaxClockMHz;
            var ram = Find(c, "ram64");
            c.DieW += 10;
            ram.X = c.DieW - ram.W;
            var far = DesignLab.Analyze(D, s, c);
            Assert.True(far.MaxClockMHz < near);
            Assert.True(far.LongestWireMm > 5);
        }

        [Fact]
        public void PowerFollowsCapacitanceVoltageAndClock()
        {
            var s = TestData.NewGame();
            var c = Chip("dip16p", "core4", "memif", "io16");
            var n = D.Node("n10um");
            c.TargetClockMHz = 0.5;
            var st = DesignLab.Analyze(D, s, c);
            double switched = c.Blocks.Sum(p => D.Block(p.BlockId).Activity * D.Block(p.BlockId).Transistors);
            double expected = switched * DesignLab.CapPerTransistor(n) * n.Voltage * n.Voltage * 0.5e6;
            Assert.Equal(expected, st.PowerW, 9);

            c.TargetClockMHz = 0.25;
            Assert.Equal(st.PowerW / 2, DesignLab.Analyze(D, s, c).PowerW, 9);
        }

        [Fact]
        public void PackagesLimitPower()
        {
            var s = TestData.NewGame();
            var c = Chip("dip16p", "core4", "memif", "io16");
            c.TargetClockMHz = 5;
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("W"));
        }

        [Fact]
        public void DesignHouseLayoutsRunSlower()
        {
            var s = TestData.NewGame();
            var house = Chip("dip16p", "core4", "memif", "io16");
            var own = house.Clone();
            own.UseDesignHouse = false;
            double ratio = DesignLab.Analyze(D, s, house).MaxClockMHz / DesignLab.Analyze(D, s, own).MaxClockMHz;
            Assert.Equal(DesignLab.DesignHouseClock, ratio, 9);
            Assert.True(DesignLab.Analyze(D, s, house).DesignHouseFee > 0);
            Assert.Equal(0, DesignLab.Analyze(D, s, own).DesignHouseFee);
        }

        [Fact]
        public void MemoryInterfaceFeedsTheCore()
        {
            var s = TestData.NewGame();
            var fed = DesignLab.Analyze(D, s, Chip("dip16p", "core4", "memif", "io16"));
            var starved = DesignLab.Analyze(D, s, Chip("dip16p", "core4", "io16"));
            Assert.True(fed.Compute > starved.Compute * 2);
            Assert.Contains(starved.Notes, n => n.Severity == 1);
        }

        [Fact]
        public void BiggerChipsCostMorePerDie()
        {
            var s = TestData.NewGame();
            var small = DesignLab.Analyze(D, s, Chip("dip40c", "core4", "memif", "io16"));
            var big = DesignLab.Analyze(D, s, Chip("dip40c", "core4", "ram64", "rom512", "memif", "io16"));
            Assert.True(big.DieAreaMm2 > small.DieAreaMm2);
            Assert.True(big.YieldEstimate < small.YieldEstimate);
            Assert.True(big.CostPerGoodDie > small.CostPerGoodDie);
        }

        [Fact]
        public void FutureBlocksAreRejected()
        {
            var s = TestData.NewGame();
            var c = Chip("dip40c", "core16", "memif", "io40");
            Assert.Contains(DesignLab.Analyze(D, s, c).Notes, n => n.Severity == 2 && n.Text.Contains("1978"));
        }
    }
}
