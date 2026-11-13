using System;
using System.Collections.Generic;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Rival companies. They reach each process node on the historical schedule plus their lag (and their own slips),
    /// design real chips for their segments with the same <see cref="DesignLab"/> maths as the player, price against the
    /// going rate (never below cost plus a small margin), grow capacity when busy, and start price wars when their fabs sit idle.
    /// </summary>
    public static class Rivals
    {
        /// <summary>A rival replaces a segment's chip after this many years even without a new node.</summary>
        public const int ProductAgeYears = 4;
        /// <summary>Silicon, validation and ramp time added to a rival's design time.</summary>
        public const int LaunchWeeks = 12;
        public const double PriceWarUtilisation = 0.6;
        /// <summary>Chips kept on sale per segment.</summary>
        public const int ProductsPerSegment = 2;
        /// <summary>Lowest price a rival will charge, as a multiple of its cost.</summary>
        public const double MinMarkup = 1.1;
        /// <summary>Smallest capacity a rival keeps, in chips a month.</summary>
        public const double MinCapacity = 2000;
        /// <summary>How far a price war can push the price factor, as a share of normal.</summary>
        public const double PriceWarFloor = 0.5;

        public static RivalState Get(GameState s, string id) => s.Rivals.FirstOrDefault(r => r.Id == id);

        public static RivalProduct Product(GameState s, string rivalId, string designId) =>
            Get(s, rivalId)?.Products.FirstOrDefault(p => p.Design.Id == designId);

        /// <summary>The newest node the rival can make chips on (has a working fab for).</summary>
        public static ProcessNodeDef BestNode(GameData d, RivalState r) =>
            d.Nodes.Where(n => r.ProductionNodes.Contains(n.Id)).OrderBy(n => n.FeatureNm).FirstOrDefault();

        /// <summary>The newest node the rival has researched (it may still be converting a fab for it).</summary>
        public static ProcessNodeDef ResearchedNode(GameData d, RivalState r) =>
            d.Nodes.Where(n => r.NodesReady.Contains(n.Id)).OrderBy(n => n.FeatureNm).FirstOrDefault();

        /// <summary>Years from a rival having a node working to producing on it: a retool plus half a ramp, like the player's.</summary>
        public static double ConversionYears(ProcessNodeDef n) =>
            (Math.Ceiling(n.BuildMonths * FabOps.RetoolTimeShare) + n.RampMonths * 0.5) / 12.0;

        public static string BinOf(RivalProduct p) => Binning.BinsFor(p.Design)[0].Name;

        /// <summary>Cost per chip now: the launch cost, inflated, minus about 7% a year of manufacturing learning (to 60%).</summary>
        public static double Cost(GameState s, RivalProduct p) =>
            p.CostReal * s.Inflation * Math.Max(0.6, Math.Pow(0.93, (s.Tick - p.ReleasedTick) / (double)Calendar.WeeksPerYear));

        /// <summary>A rival's price for a chip in a segment: the going price times its price factor, but never below cost plus <see cref="MinMarkup"/>.</summary>
        public static double Price(GameState s, RivalState r, RivalProduct p, SegmentDef seg) =>
            Math.Max(Cost(s, p) * MinMarkup, Market.ExpectedPrice(seg, s.YearF) * r.PriceFactor);

        public static void Init(GameData d, GameState s)
        {
            foreach (var def in d.Rivals)
            {
                var r = new RivalState
                {
                    Id = def.Id,
                    Name = def.Name,
                    Rep = def.Rep,
                    PriceFactor = def.PriceFactor,
                    BasePriceFactor = def.PriceFactor,
                    Capacity = Math.Max(MinCapacity, def.StartShare * def.Segments.Select(id => d.Segment(id)).Where(x => x != null).Sum(x => x.DemandAt(s.YearF))),
                    NodesReady = d.Nodes.Where(n => n.Requires == null || n.Year + def.NodeLagYears <= s.YearF).Select(n => n.Id).ToList(),
                };
                r.ProductionNodes = new List<string>(r.NodesReady);
                s.Rivals.Add(r);
                ScheduleNextNode(d, s, r, def);
            }
        }

        static void ScheduleNextNode(GameData d, GameState s, RivalState r, RivalDef def)
        {
            var best = ResearchedNode(d, r);
            var next = best == null ? null : d.Nodes.FirstOrDefault(n => n.Requires == best.Id);
            if (next == null)
            {
                r.NextNodeId = null;
                return;
            }
            double slip = s.Rng.Chance(next.SlipRisk) ? s.Rng.Range(0.25, 1.5) : 0;
            r.NextNodeId = next.Id;
            r.NextNodeDue = Math.Max(s.YearF, next.Year + def.NodeLagYears) + slip;
        }

        static bool Serves(GameData d, RivalState r, SegmentDef seg) => d.Rival(r.Id)?.Segments.Contains(seg.Id) == true;

        /// <summary>The chip a rival puts into distribution for a segment: best performance per dollar that distributors will stock.</summary>
        public static RivalProduct OpenMarketOffer(GameData d, GameState s, RivalState r, SegmentDef seg)
        {
            if (!Serves(d, r, seg)) return null;
            return r.Products
                .Where(p => Market.Qualifies(d, seg, p.Design, BinOf(p), s.YearF))
                .OrderByDescending(p => Market.Perf(p.Design, BinOf(p), seg.Metric) / Price(s, r, p, seg))
                .FirstOrDefault();
        }

        /// <summary>Each rival bids once on each open request in its segments, with its best qualifying chip, priced at cost times markup.</summary>
        public static void BidOnOpenRfqs(GameData d, GameState s)
        {
            foreach (var rfq in s.Rfqs)
            {
                var seg = d.Segment(rfq.SegmentId);
                foreach (var r in s.Rivals)
                {
                    if (!Serves(d, r, seg) || rfq.Bids.Any(b => b.BidderId == r.Id)) continue;
                    var spec = ChipSpec.ForRfq(d, rfq);
                    var p = r.Products
                        .Where(x => Specs.Check(d, spec, x.Design, BinOf(x)).Count == 0
                                    && Cost(s, x) * MinMarkup <= rfq.MaxPrice)
                        .OrderByDescending(x => Market.Perf(x.Design, BinOf(x), seg.Metric) / Cost(s, x))
                        .FirstOrDefault();
                    if (p == null) continue;
                    rfq.Bids.Add(new Bid
                    {
                        BidderId = r.Id,
                        DesignId = p.Design.Id,
                        Bin = BinOf(p),
                        Price = Math.Min(rfq.MaxPrice, Math.Max(Cost(s, p) * MinMarkup, rfq.ExpectedPrice * r.PriceFactor)),
                        SubmittedTick = s.Tick,
                    });
                }
            }
        }

        public static void MonthEnd(GameData d, GameState s)
        {
            foreach (var r in s.Rivals)
            {
                var def = d.Rival(r.Id);
                if (def == null) continue;

                r.LastMonthRevenue = r.RevenueThisMonth;
                r.TotalRevenue += r.RevenueThisMonth;
                r.LastUtilisation = r.Capacity > 0 ? r.UsedThisMonth / r.Capacity : 0;

                // capacity follows sales: fast growth when sold out, slow shrinking when idle
                double ceiling = Math.Max(MinCapacity, def.MaxShare * def.Segments.Select(id => d.Segment(id)).Where(x => x != null).Sum(x => x.DemandAt(s.YearF)));
                if (r.LastUtilisation > 0.85) r.Capacity = Math.Min(r.Capacity * 1.06, Math.Max(r.Capacity, ceiling));
                else if (r.LastUtilisation < 0.5 && r.Products.Count > 0) r.Capacity = Math.Max(MinCapacity, r.Capacity * 0.98);
                if (r.Capacity > ceiling * 1.2) r.Capacity = Math.Max(ceiling, r.Capacity * 0.97);   // demand fell away: shed capacity

                // idle fabs mean price cuts; full ones let prices recover
                if (r.Products.Count > 0 && r.LastUtilisation < PriceWarUtilisation)
                {
                    r.PriceFactor = Math.Max(r.BasePriceFactor * PriceWarFloor, r.PriceFactor * 0.97);
                    if (!r.PriceWar && r.PriceFactor < r.BasePriceFactor * 0.85)
                    {
                        r.PriceWar = true;
                        s.Log($"{r.Name} slashes its prices to fill its fabs.", 2);
                    }
                }
                else if (r.LastUtilisation > 0.9)
                {
                    r.PriceFactor = Math.Min(r.BasePriceFactor, r.PriceFactor * 1.02);
                    if (r.PriceWar && r.PriceFactor >= r.BasePriceFactor * 0.95)
                    {
                        r.PriceWar = false;
                        s.Log($"{r.Name}'s fabs are full again and its prices recover.", 0);
                    }
                }

                // the node race
                // the node race: research, then convert a fab, then produce
                if (r.ConvertingNodeId != null && s.YearF >= r.ConvertedYear)
                {
                    r.ProductionNodes.Add(r.ConvertingNodeId);
                    s.Log($"{r.Name} starts production on {d.Node(r.ConvertingNodeId).Name}.", 2);
                    r.ConvertingNodeId = null;
                }
                if (r.NextNodeId != null && r.ConvertingNodeId == null && s.YearF >= r.NextNodeDue)
                {
                    var n = d.Node(r.NextNodeId);
                    r.NodesReady.Add(n.Id);
                    r.ConvertingNodeId = n.Id;
                    r.ConvertedYear = s.YearF + ConversionYears(n);
                    int tick = (int)Math.Round((r.ConvertedYear - Calendar.StartYear) * Calendar.WeeksPerYear);
                    s.Log($"{r.Name} has its {n.Name} process working and is converting a fab: production expected around {Calendar.DateLabel(tick)}.", 2);
                    ScheduleNextNode(d, s, r, def);
                }

                // chip projects
                if (r.Project != null)
                {
                    r.Project.WeeksLeft -= Calendar.WeeksPerMonth;
                    if (r.Project.WeeksLeft <= 0) Launch(d, s, r);
                }
                else StartProject(d, s, r, def);
            }
        }

        static void StartProject(GameData d, GameState s, RivalState r, RivalDef def)
        {
            var node = BestNode(d, r);
            if (node == null) return;
            foreach (var segId in def.Segments)
            {
                var seg = d.Segment(segId);
                if (seg == null || !seg.Active(s.YearF)) continue;
                var newest = r.Products.Where(p => p.SegmentId == segId).OrderByDescending(p => p.ReleasedTick).FirstOrDefault();
                bool stale = newest == null
                             || newest.Design.NodeId != node.Id
                             || s.Tick - newest.ReleasedTick > ProductAgeYears * Calendar.WeeksPerYear;
                if (!stale) continue;

                string name = $"{r.Name.Substring(0, 1)}-{100 + r.ProductsMade + 1}";
                var c = DesignLab.PlanForSegment(d, s, node.Id, seg.Metric, name, ignoreNodeReady: true, maxPowerW: seg.MaxPowerW);
                if (c == null) continue;
                r.ProductsMade++;
                c.Id = s.NewId("rchip");
                c.Stats = DesignLab.Analyze(d, s, c, ignoreNodeReady: true);
                int weeks = (int)Math.Ceiling(c.Stats.EngineerMonths / Math.Max(1, def.Engineers) * Calendar.WeeksPerMonth) + LaunchWeeks;
                r.Project = new RivalProject { SegmentId = segId, Design = c, WeeksLeft = weeks };
                return;
            }
        }

        static void Launch(GameData d, GameState s, RivalState r)
        {
            var pj = r.Project;
            var c = pj.Design;
            c.Stats = DesignLab.Analyze(d, s, c, ignoreNodeReady: true);
            c.Stage = DesignStage.Released;
            c.ReleasedTick = s.Tick;
            r.Products.Add(new RivalProduct { Design = c, SegmentId = pj.SegmentId, ReleasedTick = s.Tick, CostReal = c.Stats.CostPerGoodDie / s.Inflation });
            r.Project = null;
            var seg = d.Segment(pj.SegmentId);
            s.Log($"{r.Name} launches the {c.Name} for {seg.Name.ToLower()}: {c.Stats.ClockMHz:0.##} MHz on {d.Node(c.NodeId).Name}.", 0);

            foreach (var g in r.Products.GroupBy(p => p.SegmentId).ToList())
                foreach (var old in g.OrderByDescending(p => p.ReleasedTick).Skip(ProductsPerSegment).ToList())
                    r.Products.Remove(old);
        }
    }
}
