using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class MarketTests
    {
        static GameData D => TestData.Load();

        /// <summary>A chip for a segment, marked released as if it had come through the design process.</summary>
        static ChipDesign ReleasedChip(GameState s, string nodeId, Metric metric, string name = "T")
        {
            double watts = D.Segments.First(x => x.Metric == metric).MaxPowerW;
            var c = DesignLab.PlanForSegment(D, s, nodeId, metric, name, maxPowerW: watts);
            Assert.NotNull(c);
            return Release(s, c);
        }

        static ChipDesign Release(GameState s, ChipDesign c)
        {
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            Assert.True(c.Stats.Valid, c.Stats.FirstError);
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            s.Designs.Add(c);
            return c;
        }

        [Fact]
        public void MarketDataLoads()
        {
            Assert.Equal(3, D.Segments.Count);
            Assert.True(D.Customers.Count >= 8);
            Assert.Equal(2, D.Rivals.Count);
            Assert.Empty(D.Validate());
        }

        [Fact]
        public void NewGamesSetUpCustomersAndRivals()
        {
            var s = TestData.NewGame();
            Assert.Equal(D.Customers.Count, s.Customers.Count);
            Assert.Equal(2, s.Rivals.Count);
            Assert.All(s.Rivals, r => Assert.Contains("n10um", r.NodesReady));
        }

        [Fact]
        public void FinishedLotsGoToTheWarehouse()
        {
            var s = TestData.NewGame();
            var c = Release(s, DesignLab.Starter(D, "T", "n10um", "dip16p", "core4", "memif", "io16"));
            var lot = new WaferLot { Done = true, DesignId = c.Id, GrossDies = 120, Bins = new Dictionary<string, int> { { "Standard", 100 }, { Binning.Scrap, 20 } } };
            var f = new Fab { Id = "f1", Name = "F", NodeId = "n10um" };
            f.Lots.Add(lot);
            s.Fabs.Add(f);
            Market.ReceiveLots(s);
            Assert.Equal(100, Market.Stock(s, c.Id, "Standard"), 6);
            Assert.True(lot.Delivered);
        }

        static double SellOneMonth(double priceFactor)
        {
            var s = TestData.NewGame(year: 1975);
            var c = ReleasedChip(s, "n6um", Metric.Control);
            Market.AddStock(s, c.Id, "Standard", 1e8);
            double price = Market.ExpectedPrice(D.Segment("embedded"), s.YearF) * priceFactor;
            Assert.Null(Market.SetListing(s, c.Id, "Standard", price));
            Simulation.AdvanceMonths(D, s, 1);
            return s.MarketHistory.Where(r => r.SegmentId == "embedded").Sum(r => r.PlayerUnits);
        }

        [Fact]
        public void ListedChipsSellInTheOpenMarket()
        {
            Assert.True(SellOneMonth(1.0) > 0);
        }

        [Fact]
        public void HigherPricesSellLess()
        {
            Assert.True(SellOneMonth(3.0) < SellOneMonth(1.0) * 0.5);
        }

        [Fact]
        public void DistributorsWontStockWeakChips()
        {
            var s = TestData.NewGame(year: 1984);
            var c = Release(s, DesignLab.Starter(D, "Old", "n10um", "dip16p", "core4", "memif", "io16"));
            Market.AddStock(s, c.Id, "Standard", 1e6);
            Market.SetListing(s, c.Id, "Standard", 1);
            Simulation.AdvanceMonths(D, s, 1);
            Assert.Equal(1e6, Market.Stock(s, c.Id, "Standard"), 6);
        }

        [Fact]
        public void BidsMustMeetTheRequest()
        {
            var s = TestData.NewGame(year: 1984);
            var weak = Release(s, DesignLab.Starter(D, "Old", "n10um", "dip16p", "core4", "memif", "io16"));
            var rfq = new Rfq { Id = "r1", CustomerId = "brightline", SegmentId = "embedded", MinMetric = 2, MaxPowerW = 1, MaxPrice = 10, ExpectedPrice = 7 };
            s.Rfqs.Add(rfq);
            Assert.Contains("needs", Market.SubmitBid(D, s, "r1", weak.Id, "Standard", 5));
            Assert.Contains("won't pay", Market.SubmitBid(D, s, "r1", weak.Id, "Standard", 50));
            Assert.Contains("closed", Market.SubmitBid(D, s, "nope", weak.Id, "Standard", 5));
        }

        [Fact]
        public void WinningABidCreatesAContract()
        {
            var s = TestData.NewGame(year: 1975);
            for (int i = 0; i < 40 && !s.Rfqs.Any(r => r.SegmentId == "embedded"); i++) Simulation.AdvanceMonths(D, s, 1);
            var rfq = s.Rfqs.First(r => r.SegmentId == "embedded");
            var c = ReleasedChip(s, "n6um", Metric.Control);
            Assert.Null(Market.SubmitBid(D, s, rfq.Id, c.Id, "Standard", rfq.ExpectedPrice * 0.05));   // far below anyone's cost: wins regardless of rivals
            for (int i = 0; i < 6 && s.Rfqs.Contains(rfq); i++) Simulation.AdvanceMonths(D, s, 1);
            Assert.DoesNotContain(rfq, s.Rfqs);
            var k = Assert.Single(s.Contracts, x => x.CustomerId == rfq.CustomerId && x.SupplierId == Market.Player);
            Assert.Equal(rfq.StartMonth, k.StartMonth);
            Assert.Equal(Market.Player, Market.CustomerOf(s, rfq.CustomerId).SupplierId);
            Assert.True(s.Rep > 0);
        }

        static Contract AddContract(GameState s, ChipDesign c, string bin)
        {
            var k = new Contract
            {
                Id = "k1", CustomerId = "brightline", SupplierId = Market.Player, DesignId = c.Id, Bin = bin,
                BasePrice = 5, StartMonth = s.MonthIndex, EndMonth = s.MonthIndex + 12,
            };
            s.Contracts.Add(k);
            return k;
        }

        [Fact]
        public void ContractsDeliverAndPenaliseShortfalls()
        {
            var s = TestData.NewGame(year: 1975);
            var c = ReleasedChip(s, "n6um", Metric.Control);
            var k = AddContract(s, c, "Standard");
            Market.AddStock(s, c.Id, "Standard", 1e8);

            Simulation.AdvanceMonths(D, s, 1);
            Assert.True(k.LastMonthOrdered > 0);
            Assert.Equal(k.LastMonthOrdered, k.LastMonthDelivered, 6);
            Assert.False(s.Ledger.LastMonth.Lines.ContainsKey(Accounts.Penalties));
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.Sales] > 0);

            s.Inventory.Clear();
            double rep = s.Rep;
            Simulation.AdvanceMonths(D, s, 1);
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.Penalties] < 0);
            Assert.True(s.Rep < rep);
            Assert.Equal(1, k.ShortMonths);

            Simulation.AdvanceMonths(D, s, 2);
            Assert.Equal(ContractStatus.Cancelled, k.Status);
        }

        [Fact]
        public void ContractsCapMonthlyOrders()
        {
            var s = TestData.NewGame(year: 1975);
            var c = ReleasedChip(s, "n6um", Metric.Control);
            var k = AddContract(s, c, "Standard");
            k.MaxMonthly = 1000;
            Market.AddStock(s, c.Id, "Standard", 1e8);
            Simulation.AdvanceMonths(D, s, 1);
            Assert.Equal(1000, k.LastMonthOrdered, 6);
            Assert.Equal(1000, k.LastMonthDelivered, 6);
        }

        [Fact]
        public void RivalsPriceAgainstTheGoingRateNotBareCost()
        {
            var s = TestData.NewGame();
            Simulation.AdvanceMonths(D, s, 4 * 12);
            var seg = D.Segment("embedded");
            var r = Rivals.Get(s, "meridian");
            var p = Rivals.OpenMarketOffer(D, s, r, seg);
            Assert.NotNull(p);
            double price = Rivals.Price(s, r, p, seg);
            Assert.True(price >= Rivals.Cost(s, p) * Rivals.MinMarkup - 1e-9);
            Assert.True(price >= Market.ExpectedPrice(seg, s.YearF) * r.BasePriceFactor * Rivals.PriceWarFloor - 1e-9);
        }

        [Fact]
        public void RivalCapacityStartsInProportionToDemand()
        {
            var s = TestData.NewGame();
            double embedded = D.Segment("embedded").DemandAt(s.YearF);
            var m = Rivals.Get(s, "meridian");
            Assert.InRange(m.Capacity, Rivals.MinCapacity, System.Math.Max(Rivals.MinCapacity, embedded));
        }

        [Fact]
        public void DownBinningFillsOrdersFromBetterBins()
        {
            var s = TestData.NewGame(year: 1975);
            var c = ReleasedChip(s, "n6um", Metric.Control);
            c.Bins = new List<BinDef>
            {
                new BinDef { Name = "Fast", MinCores = 1, MinClockMHz = c.Stats.ClockMHz },
                new BinDef { Name = "Slow", MinCores = 1, MinClockMHz = c.Stats.ClockMHz * 0.8 },
            };
            var k = AddContract(s, c, "Slow");
            Market.AddStock(s, c.Id, "Fast", 1e8);
            Simulation.AdvanceMonths(D, s, 1);
            Assert.True(k.LastMonthDelivered > 0);
            Assert.Equal(k.LastMonthOrdered, k.LastMonthDelivered, 6);
            Assert.True(Market.Stock(s, c.Id, "Fast") < 1e8);
        }

        [Fact]
        public void RivalsClimbTheNodeLadderAndLaunchChips()
        {
            var s = TestData.NewGame();
            Simulation.AdvanceMonths(D, s, 8 * 12);
            Assert.All(s.Rivals, r => Assert.NotEmpty(r.Products));
            Assert.True(Rivals.Get(s, "meridian").NodesReady.Count >= 4);
            Assert.All(s.Rivals, r => Assert.True(r.TotalRevenue > 0, $"{r.Name} sold nothing"));
            Assert.Contains(s.News, n => n.Text.Contains("launches the"));
        }

        [Fact]
        public void CustomersRunBidsEveryCycle()
        {
            var s = TestData.NewGame();
            Simulation.AdvanceMonths(D, s, 6 * 12);
            // every embedded customer active since the start has awarded at least one contract (to someone)
            foreach (var cust in D.Customers.Where(x => x.SegmentId == "embedded" && x.From <= 1972))
                Assert.True(Market.CustomerOf(s, cust.Id).ContractEndMonth > Calendar.MonthIndex(Calendar.TickOf(1971)), cust.Id);
            Assert.True(s.MarketHistory.Count > 0);
        }

        [Fact]
        public void SavesKeepTheMarketDeterministic()
        {
            var a = TestData.NewGame(seed: 5);
            Simulation.AdvanceMonths(D, a, 36);
            var b = SaveGame.Deserialize(SaveGame.Serialize(a));
            Simulation.AdvanceMonths(D, a, 36);
            Simulation.AdvanceMonths(D, b, 36);
            Assert.Equal(SaveGame.Serialize(a), SaveGame.Serialize(b));
        }
    }
}
