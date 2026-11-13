using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class AdvisorTests
    {
        static GameData D => TestData.Load();

        static ChipDesign Released(GameState s, string nodeId = "n10um")
        {
            var c = DesignLab.PlanForSegment(D, s, nodeId, Metric.Control, "AD-1", maxPowerW: 1.0);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            s.Designs.Add(c);
            return c;
        }

        [Fact]
        public void ANewGameHasNothingSerious()
        {
            var s = TestData.NewGame();
            Assert.DoesNotContain(Advisor.Alerts(D, s), a => a.Severity >= 2);
        }

        [Fact]
        public void WarnsWhenAChipHasNoFab()
        {
            var s = TestData.NewGame();
            var c = Released(s);
            Assert.Contains(Advisor.Alerts(D, s), a => a.Text.Contains("no 10 µm fab") && a.Tab == "Fabs");
        }

        [Fact]
        public void WarnsAboutUnlistedStock()
        {
            var s = TestData.NewGame();
            var c = Released(s);
            Market.AddStock(s, c.Id, "Standard", 50_000);
            Assert.Contains(Advisor.Alerts(D, s), a => a.Text.Contains("isn't listed"));
        }

        [Fact]
        public void WarnsWhenStockPilesUp()
        {
            var s = TestData.NewGame();
            var c = Released(s);
            Market.AddStock(s, c.Id, "Standard", 100_000);
            var item = Market.StockOf(s, c.Id, "Standard");
            item.MadeLastMonth = 40_000;
            item.SoldLastMonth = 10_000;
            Market.SetListing(s, c.Id, "Standard", 10);
            Assert.Contains(Advisor.Alerts(D, s), a => a.Text.Contains("months of") && a.Text.Contains("stock"));
        }

        [Fact]
        public void TracksMadeAndSoldEachMonth()
        {
            var s = TestData.NewGame(year: 1972);
            var c = Released(s, "n8um");
            Market.AddStock(s, c.Id, "Standard", 1e6);
            Market.SetListing(s, c.Id, "Standard", Market.ExpectedPrice(D.Segment("embedded"), s.YearF));
            Simulation.AdvanceMonths(D, s, 1);
            var item = Market.StockOf(s, c.Id, "Standard");
            Assert.Equal(1e6, item.MadeLastMonth, 6);
            Assert.True(item.SoldLastMonth > 0);
            Assert.Equal(0, item.MadeThisMonth);
            Simulation.AdvanceMonths(D, s, 1);
            Assert.Equal(0, item.MadeLastMonth);
        }

        [Fact]
        public void LostBidsSayWhatTheWinnerCharged()
        {
            var s = TestData.NewGame(year: 1975);
            var c = Released(s, "n6um");
            var rfq = new Rfq
            {
                Id = "rq", CustomerId = "brightline", SegmentId = "embedded", OpenMonth = s.MonthIndex, CloseMonth = s.MonthIndex,
                StartMonth = s.MonthIndex + 1, LengthMonths = 12, MinMetric = 0.01, MaxPowerW = 5, MaxPrice = 100, ExpectedPrice = 10, EstimatedUnits = 10000,
            };
            s.Rfqs.Add(rfq);
            Assert.Null(Market.SubmitBid(D, s, "rq", c.Id, "Standard", 50));
            Rivals.Get(s, "kestrel").Products.Add(new RivalProduct { Design = c, SegmentId = "embedded", ReleasedTick = s.Tick, CostReal = 0.1 });
            rfq.Bids.Add(new Bid { BidderId = "kestrel", DesignId = c.Id, Bin = "Standard", Price = 3 });
            Simulation.AdvanceMonths(D, s, 1);
            Assert.Contains(s.News, n => n.Text.Contains("Kestrel Devices won at $3") && n.Text.Contains("you bid $50"));
        }
    }
}
