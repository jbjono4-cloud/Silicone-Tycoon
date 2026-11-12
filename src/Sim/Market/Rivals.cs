using System;
using System.Linq;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// Rival companies. They reach each process node on the historical schedule plus their lag (and their own slips),
    /// design real chips for their segments with the same <see cref="DesignLab"/> maths as the player, price at cost times
    /// a markup, grow capacity when busy, and start price wars when their fabs sit idle.
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

        public static RivalState Get(GameState s, string id) => s.Rivals.FirstOrDefault(r => r.Id == id);

        public static RivalProduct Product(GameState s, string rivalId, string designId) =>
            Get(s, rivalId)?.Products.FirstOrDefault(p => p.Design.Id == designId);

        public static ProcessNodeDef BestNode(GameData d, RivalState r) =>
            d.Nodes.Where(n => r.NodesReady.Contains(n.Id)).OrderBy(n => n.FeatureNm).FirstOrDefault();

        public static string BinOf(RivalProduct p) => Binning.BinsFor(p.Design)[0].Name;

        /// <summary>Cost per chip now: the launch cost, inflated, minus about 7% a year of manufacturing learning (to 60%).</summary>
        public static double Cost(GameState s, RivalProduct p) =>
            p.CostReal * s.Inflation * Math.Max(0.6, Math.Pow(0.93, (s.Tick - p.ReleasedTick) / (double)Calendar.WeeksPerYear));

        public static double Price(GameState s, RivalState r, RivalProduct p) => Cost(s, p) * r.Markup;

        public static void Init(GameData d, GameState s)
        {
            foreach (var def in d.Rivals)
            {
                var r = new RivalState
                {
                    Id = def.Id,
                    Name = def.Name,
                    Rep = def.Rep,
                    Markup = def.Markup,
                    BaseMarkup = def.Markup,
                    Capacity = def.StartCapacity,
                    NodesReady = d.Nodes.Where(n => n.Requires == null || n.Year + def.NodeLagYears <= s.YearF).Select(n => n.Id).ToList(),
                };
                s.Rivals.Add(r);
                ScheduleNextNode(d, s, r, def);
            }
        }

        static void ScheduleNextNode(GameData d, GameState s, RivalState r, RivalDef def)
        {
            var best = BestNode(d, r);
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
            double need = seg.NeedAt(s.YearF);
            return r.Products
                .Where(p => Market.Perf(p.Design, BinOf(p), seg.Metric) >= need * Market.OpenMarketMinShare && Market.Power(p.Design, BinOf(p)) <= seg.MaxPowerW)
                .OrderByDescending(p => Market.Perf(p.Design, BinOf(p), seg.Metric) / Price(s, r, p))
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
                    var p = r.Products
                        .Where(x => Market.Perf(x.Design, BinOf(x), seg.Metric) >= rfq.MinMetric
                                    && Market.Power(x.Design, BinOf(x)) <= rfq.MaxPowerW
                                    && Cost(s, x) * 1.05 <= rfq.MaxPrice)
                        .OrderByDescending(x => Market.Perf(x.Design, BinOf(x), seg.Metric) / Cost(s, x))
                        .FirstOrDefault();
                    if (p == null) continue;
                    rfq.Bids.Add(new Bid
                    {
                        BidderId = r.Id,
                        DesignId = p.Design.Id,
                        Bin = BinOf(p),
                        Price = Math.Min(rfq.MaxPrice, Cost(s, p) * r.Markup),
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

                // capacity follows sales
                if (r.LastUtilisation > 0.85) r.Capacity *= 1.04;
                else if (r.LastUtilisation < 0.5 && r.Products.Count > 0) r.Capacity = Math.Max(def.StartCapacity * 0.5, r.Capacity * 0.98);

                // idle fabs mean price cuts; full ones let prices recover
                if (r.Products.Count > 0 && r.LastUtilisation < PriceWarUtilisation)
                {
                    r.Markup = Math.Max(1.08, r.Markup * 0.97);
                    if (!r.PriceWar && r.Markup < r.BaseMarkup * 0.85)
                    {
                        r.PriceWar = true;
                        s.Log($"{r.Name} slashes its prices to fill its fabs.", 2);
                    }
                }
                else if (r.LastUtilisation > 0.9)
                {
                    r.Markup = Math.Min(r.BaseMarkup, r.Markup * 1.02);
                    if (r.PriceWar && r.Markup >= r.BaseMarkup * 0.95) r.PriceWar = false;
                }

                // the node race
                if (r.NextNodeId != null && s.YearF >= r.NextNodeDue)
                {
                    r.NodesReady.Add(r.NextNodeId);
                    s.Log($"{r.Name} starts production on {d.Node(r.NextNodeId).Name}.", 0);
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
