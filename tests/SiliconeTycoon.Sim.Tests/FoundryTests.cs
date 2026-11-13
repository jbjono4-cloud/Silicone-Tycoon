using System;
using System.Linq;
using Xunit;

namespace SiliconeTycoon.Sim.Tests
{
    public class FoundryTests
    {
        static GameData D => TestData.Load();

        static GameState Game(int year, StartType start = StartType.Integrated, double cash = 50_000_000) =>
            Simulation.NewGame(D, new NewGameOptions { EventRate = 0, StartYear = year, StartingCash = cash, StartType = start });

        static ChipDesign Released(GameState s, string nodeId, Metric metric = Metric.Control)
        {
            var seg = D.Segments.First(x => x.Metric == metric);
            var c = DesignLab.PlanForSegment(D, s, nodeId, metric, "F-" + s.NextId, ignoreNodeReady: true, maxPowerW: seg.MaxPowerW);
            Assert.NotNull(c);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c, ignoreNodeReady: true);
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            s.Designs.Add(c);
            return c;
        }

        static Fab RunningFab(GameState s, string node = "n10um", int capacity = 2000)
        {
            Assert.Null(FabOps.Build(D, s, null, node, "us_ca", capacity));
            var f = s.Fabs.Last();
            while (f.Stage != FabStage.Running) Simulation.Advance(D, s);
            f.Learning = 30;   // a well-run line, so quality isn't what's being tested
            return f;
        }

        [Fact]
        public void TheFoundryDataLoads()
        {
            Assert.NotEmpty(D.Foundries);
            Assert.Contains(D.Clients, c => c.Kind == ClientKind.Wafers);
            Assert.Contains(D.Clients, c => c.Kind == ClientKind.Asic);
            Assert.Empty(D.Validate());
        }

        [Fact]
        public void FoundriesLetYouDesignForNodesYouHaventResearched()
        {
            var s = Simulation.NewGame(D, new NewGameOptions { EventRate = 0, StartYear = 1982, NodesReady = new System.Collections.Generic.List<string> { "n10um" } });
            Assert.False(s.NodeReady("n5um"));
            Assert.True(Foundry.CanDesignFor(D, s, "n5um"));                  // Orchard and Sendai run it by now
            Assert.False(Foundry.CanDesignFor(D, s, "n1_5um"));
        }

        [Fact]
        public void FablessCompaniesMakeChipsAtAFoundry()
        {
            var s = Game(1980, StartType.Fabless);
            Assert.Empty(s.Fabs);
            var c = Released(s, "n6um");
            var q = Foundry.QuoteSupply(D, s, "orchard", "n6um", 500, 24);
            Assert.Null(q.Problem);
            Assert.True(q.Price > Foundry.CostToMake(s, D.Node("n6um")));
            Assert.Null(Foundry.SignSupply(D, s, "orchard", "n6um", 500, 24));
            var f = s.Fabs.Single();
            Assert.True(f.External);
            Assert.Null(FabOps.SetAllocation(D, s, f, c.Id, 500));
            Simulation.AdvanceMonths(D, s, Foundry.SupplyLeadMonths + 3);
            Assert.Equal(FabStage.Running, f.Stage);
            Assert.True(s.Inventory.Where(x => x.DesignId == c.Id).Sum(x => x.MadeLastMonth) > 0);
            var lines = s.Ledger.LastMonth.Lines;
            Assert.True(lines[Accounts.FoundryWafers] < 0);
            Assert.False(lines.ContainsKey(Accounts.FabRunning));
            Assert.False(lines.ContainsKey(Accounts.Depreciation));
        }

        [Fact]
        public void UnusedFoundryWafersStillCost()
        {
            var s = Game(1980, StartType.Fabless);
            Foundry.SignSupply(D, s, "orchard", "n6um", 500, 12);
            var f = s.Fabs.Single();
            Simulation.AdvanceMonths(D, s, Foundry.SupplyLeadMonths + 2);
            Assert.Equal(FabStage.Running, f.Stage);
            double unused = s.Ledger.LastMonth.Lines[Accounts.UnusedWafers];
            Assert.InRange(-unused, 0.5 * 500 * Foundry.MinTakeShare * f.WaferPrice - 1, 500 * Foundry.MinTakeShare * f.WaferPrice + 1);   // less while the foundry rations
        }

        [Fact]
        public void EndingAnAgreementEarlyCostsAndItCanBeRenewed()
        {
            var s = Game(1980, StartType.Fabless);
            Foundry.SignSupply(D, s, "orchard", "n6um", 500, 12);
            var f = s.Fabs.Single();
            int end = f.SupplyEndMonth;
            Assert.Null(Foundry.RenewSupply(D, s, f, 12));
            Assert.Equal(end + 12, f.SupplyEndMonth);
            double cost = Foundry.CancelCost(s, f);
            Assert.True(cost > 0);
            double cash = s.Cash;
            Assert.Null(Foundry.CancelSupply(D, s, f));
            Assert.Equal(cash - cost, s.Cash, 3);
            Assert.Equal(FabStage.Closed, f.Stage);
        }

        [Fact]
        public void AgreementsRunOut()
        {
            var s = Game(1980, StartType.Fabless);
            Foundry.SignSupply(D, s, "orchard", "n6um", 500, 12);
            var f = s.Fabs.Single();
            Simulation.AdvanceMonths(D, s, Foundry.SupplyLeadMonths + 13);
            Assert.Equal(FabStage.Closed, f.Stage);
        }

        [Fact]
        public void FoundriesCantSellMoreThanTheyHave()
        {
            var s = Game(1980, StartType.Fabless);
            var q = Foundry.QuoteSupply(D, s, "orchard", "n6um", 1_000_000, 24);
            Assert.Contains("free", q.Problem);
            Assert.NotNull(Foundry.QuoteSupply(D, s, "orchard", "n1_5um", 500, 24).Problem);
            Assert.NotNull(Foundry.QuoteSupply(D, s, "formosa", "n6um", 500, 24).Problem);   // not open until 1985
        }

        [Fact]
        public void ScarceChipsSqueezeFablessSupply()
        {
            var s = Game(1980, StartType.Fabless);
            Foundry.SignSupply(D, s, "orchard", "n6um", 500, 24);
            var f = s.Fabs.Single();
            Simulation.AdvanceMonths(D, s, 1);
            foreach (var seg in D.Segments) s.MarketHistory.Add(new MarketRecord { Month = s.MonthIndex, SegmentId = seg.Id, Shortage = 1 });
            Foundry.MonthEnd(D, s);
            Assert.True(f.SupplyFactor < 0.9);
        }

        static WaferRfq WaitForWaferRfq(GameState s)
        {
            for (int i = 0; i < 60 && s.WaferRfqs.Count == 0; i++) Simulation.AdvanceMonths(D, s, 1);
            Assert.NotEmpty(s.WaferRfqs);
            return s.WaferRfqs[0];
        }

        [Fact]
        public void FoundriesWinAndMakeOtherCompaniesWafers()
        {
            var s = Game(1975);
            var f = RunningFab(s, "n6um", 3000);
            var r = WaitForWaferRfq(s);
            double price = Math.Round(r.ReferencePrice * 0.9, 2);
            Assert.Null(Foundry.BidWafers(D, s, r.Id, f.Id, price));
            Assert.True(Foundry.WaferScore(D, s, r, f, price) > Foundry.WaferOthersScore(r));
            while (s.WaferRfqs.Contains(r)) Simulation.AdvanceMonths(D, s, 1);
            var k = Assert.Single(s.WaferContracts);
            Assert.Equal(f.Id, k.FabId);
            while (s.MonthIndex < k.StartMonth + 3) Simulation.AdvanceMonths(D, s, 1);
            Assert.Contains(f.Allocations, a => a.ContractId == k.Id && a.Starts == (int)k.Wafers);
            Assert.True(k.Delivered > 0);
            Assert.True(k.Revenue > 0);
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.FoundryRevenue] > 0);
            Assert.True(f.PlannedStarts <= f.Capacity);
        }

        [Fact]
        public void ClientsDistrustFoundriesThatCompeteWithThem()
        {
            var s = Game(1975);
            var f = RunningFab(s, "n6um", 3000);
            var r = WaitForWaferRfq(s);
            double price = r.ReferencePrice;
            double pure = Foundry.WaferScore(D, s, r, f, price);
            var c = Released(s, "n6um");
            Market.SetListing(s, c.Id, "Standard", 10);
            Assert.True(Foundry.SellsOwnChips(s));
            Assert.Equal(pure * Foundry.ConflictPenalty, Foundry.WaferScore(D, s, r, f, price), 9);
        }

        [Fact]
        public void WaferBidsNeedAFineEnoughProcess()
        {
            var s = Game(1975);
            var f = RunningFab(s, "n10um");
            var r = WaitForWaferRfq(s);
            r.MaxFeatureNm = 6000;
            Assert.Contains("finer", Foundry.CheckWaferBid(D, s, r, f, r.ReferencePrice));
            Assert.Contains("won't pay", Foundry.CheckWaferBid(D, s, r, RunningFab(s, "n6um"), r.MaxPrice * 2));
        }

        static AsicDeal WaitForAsic(GameState s)
        {
            for (int i = 0; i < 60 && !s.AsicDeals.Any(x => x.Status == AsicStatus.Offered); i++) Simulation.AdvanceMonths(D, s, 1);
            var deal = s.AsicDeals.FirstOrDefault(x => x.Status == AsicStatus.Offered);
            Assert.NotNull(deal);
            return deal;
        }

        [Fact]
        public void CustomChipsPayFeesAndBecomeSupplyContracts()
        {
            var s = Game(1976);
            var deal = WaitForAsic(s);
            Assert.True(deal.Nre > 0 && deal.UnitPrice > 0 && deal.MonthlyUnits > 0);
            double cash = s.Cash;
            Assert.Null(Foundry.AcceptAsic(D, s, deal.Id));
            Assert.Equal(cash + deal.Nre * Foundry.NreUpfront, s.Cash, 3);

            var node = Foundry.BestDesignNode(D, s);
            var c = DesignLab.PlanForSpec(D, s, node.Id, deal.Spec, "ASIC-1", out string why);
            Assert.True(c != null, why);
            c.Id = s.NewId("chip");
            c.Stats = DesignLab.Analyze(D, s, c);
            s.Designs.Add(c);
            Assert.Null(Foundry.AssignChip(s, deal.Id, c));
            Assert.Equal(deal.Id, c.CustomFor);
            Assert.NotNull(Market.SetListing(s, c.Id, "Standard", 5));       // only the client may buy it
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            cash = s.Cash;
            Simulation.AdvanceMonths(D, s, 1);
            Assert.Equal(AsicStatus.Supplying, deal.Status);
            var k = s.Contracts.Single(x => x.Id == deal.ContractId);
            Assert.Equal(deal.MonthlyUnits, k.FixedMonthly);
            Assert.True(s.Ledger.LastMonth.Lines[Accounts.Nre] > 0);

            Market.AddStock(s, c.Id, k.Bin, deal.MonthlyUnits * 3);
            Simulation.AdvanceMonths(D, s, 2);
            Assert.Equal(deal.MonthlyUnits, k.LastMonthOrdered, 6);
            Assert.Equal(deal.MonthlyUnits, k.LastMonthDelivered, 6);
        }

        [Fact]
        public void MissingACustomChipDeadlineLosesTheDeal()
        {
            var s = Game(1976);
            var deal = WaitForAsic(s);
            Foundry.AcceptAsic(D, s, deal.Id);
            double rep = s.Rep;
            while (s.MonthIndex <= deal.DeadlineMonth + 1) Simulation.AdvanceMonths(D, s, 1);
            Assert.Equal(AsicStatus.Failed, deal.Status);
            Assert.True(s.Rep < rep);
        }

        [Fact]
        public void SellingAFabBringsCashAndCanKeepItsWafers()
        {
            var s = Game(1980);
            var f = RunningFab(s, "n6um", 2000);
            var c = Released(s, "n6um");
            FabOps.AssignDesign(D, s, f, c.Id);
            var q = Foundry.QuoteSale(D, s, f);
            Assert.Null(q.Problem);
            Assert.True(q.Price > 0);
            double cash = s.Cash, buyerBefore = Foundry.Capacity(s, q.Buyer);
            Assert.Null(Foundry.SellFab(D, s, f, 800, 24));
            Assert.Equal(cash + q.Price, s.Cash, 3);
            Assert.True(f.Sold);
            Assert.Equal(FabStage.Closed, f.Stage);
            Assert.True(Foundry.Capacity(s, q.Buyer) >= buyerBefore + 2000 - 1e-6);
            var lease = s.Fabs.Single(x => x.External);
            Assert.Equal(800, lease.Capacity);
            Assert.Equal(FabStage.Running, lease.Stage);
            Assert.True(lease.Makes(c.Id));
            Assert.Equal(f.Learning, lease.Learning, 6);
        }

        [Fact]
        public void StartTypesShapeTheCompany()
        {
            var fabless = Game(1970, StartType.Fabless);
            var integrated = Game(1970);
            var foundry = Game(1970, StartType.Foundry);
            Assert.Empty(fabless.Fabs);
            Assert.True(fabless.DesignTeams[0].Engineers > integrated.DesignTeams[0].Engineers);
            Assert.True(Foundry.QuoteSupply(D, fabless, "meridian_ws", "n10um", 300, 24).Problem == null || fabless.YearF < 1970.5);
            var f = Assert.Single(foundry.Fabs);
            Assert.Equal(FabStage.Running, f.Stage);
            Assert.True(f.Learning > 0);
            Assert.Equal("Fab owner without products yet", Foundry.Model(foundry));
            Assert.Equal("Start-up", Foundry.Model(integrated));
        }

        [Fact]
        public void TheFoundryBusinessSurvivesASave()
        {
            var s = Game(1976, StartType.Foundry);
            Simulation.AdvanceMonths(D, s, 24);
            var b = SaveGame.Deserialize(SaveGame.Serialize(s));
            Simulation.AdvanceMonths(D, s, 12);
            Simulation.AdvanceMonths(D, b, 12);
            Assert.Equal(SaveGame.Serialize(s), SaveGame.Serialize(b));
        }
    }
}
