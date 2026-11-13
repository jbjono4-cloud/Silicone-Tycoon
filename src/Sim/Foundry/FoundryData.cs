using System.Collections.Generic;

namespace SiliconeTycoon.Sim
{
    /// <summary>
    /// A company that sells wafers to others: an independent foundry, or an integrated rival's wafer-services arm.
    /// Capacity is wafers a month offered to outside customers in its first year, growing by Growth a year.
    /// </summary>
    public class FoundryDef
    {
        public string Id;
        public string Name;
        /// <summary>Set for an integrated rival's wafer services (it also competes with you).</summary>
        public string RivalId;
        public string LocationId;
        public int From = 1970;
        public int To = 9999;
        /// <summary>Years behind history a node reaches its fabs.</summary>
        public double NodeLagYears = 1;
        /// <summary>Markup over what a wafer costs it to make.</summary>
        public double Margin = 0.3;
        public double Capacity = 1000;
        public double Growth = 0.1;
        /// <summary>Months of process learning its fabs have when a node is first offered.</summary>
        public double MaturityMonths = 12;
        /// <summary>Makes no chips of its own (customers trust it with their designs).</summary>
        public bool PurePlay;
    }

    public enum ClientKind { Wafers, Asic }

    /// <summary>
    /// A company that buys from foundries: wafer clients want processed wafers of their own designs; ASIC clients want a
    /// chip designed to their spec and then supplied.
    /// </summary>
    public class ClientDef
    {
        public string Id;
        public string Name;
        public string Country;
        public ClientKind Kind;
        /// <summary>ASIC clients: the market their product is in (sets the metric, features and prices).</summary>
        public string SegmentId;
        public int From = 1970;
        public int To = 9999;
        /// <summary>Wafer clients: wafers a month they need, [[year, wafers], ...].</summary>
        public double[][] Wafers;
        /// <summary>ASIC clients: chips a month their product needs, [[year, units], ...].</summary>
        public double[][] Units;
        /// <summary>Wafer clients: the newest node they need is at most this many years older than history's newest.</summary>
        public double MaxNodeAgeYears = 3;
        /// <summary>Most they'll pay over the reference price (wafers) or the market price (ASIC chips).</summary>
        public double PriceFactor = 1.3;
        public int CycleMonths = 24;
        public int LeadMonths = 3;
        /// <summary>ASIC clients: months between offers.</summary>
        public int EveryMonths = 30;
        /// <summary>Wafer clients: their typical die, for the yield clause.</summary>
        public double DieMm2 = 15;
        /// <summary>Wafer clients: worst defect density they accept, as a multiple of the node's mature level.</summary>
        public double QualityFactor = 1.5;
        public double Loyalty = 0.1;

        public bool Active(int year) => year >= From && year <= To;
    }

    // ---------------------------------------------------------------- state

    public class FoundryState
    {
        public string Id;
        /// <summary>Wafers a month added by fabs it bought.</summary>
        public double BoughtCapacity;
        /// <summary>Learning (months) its bought fabs brought, by node.</summary>
        public Dictionary<string, double> BoughtLearning = new Dictionary<string, double>();
    }

    public class ClientState
    {
        public string Id;
        public int NextRfqMonth;
        public int NextAsicMonth;
        public double Satisfaction = 0.5;
        /// <summary>Who makes their wafers or chips now: "player", "others" or a foundry id.</summary>
        public string SupplierId = "others";
    }

    /// <summary>A wafer client's request for quotes: so many wafers a month on a node at least this new.</summary>
    public class WaferRfq
    {
        public string Id;
        public string ClientId;
        public int OpenMonth;
        public int CloseMonth;
        public int StartMonth;
        public int LengthMonths;
        /// <summary>Largest feature size acceptable (nm): any node at least this fine will do.</summary>
        public double MaxFeatureNm;
        public double Wafers;
        public double MaxPrice;
        /// <summary>What wafers on that node usually cost from a foundry.</summary>
        public double ReferencePrice;
        /// <summary>Worst defect density accepted, per cm².</summary>
        public double MaxD0;
        public double DieMm2;
        /// <summary>Your bid (only the player bids; independent foundries are the baseline to beat).</summary>
        public string BidFabId;
        public double BidPrice;
    }

    public enum WaferContractStatus { Active, Completed, Cancelled }

    /// <summary>Wafers you make for a client each month from one of your fabs, paid per finished wafer.</summary>
    public class WaferContract
    {
        public string Id;
        public string ClientId;
        public string FabId;
        public string NodeId;
        public double Wafers;
        public double Price;
        public double MaxD0;
        public double DieMm2;
        public int StartMonth;
        public int EndMonth;
        public WaferContractStatus Status;
        public int StartedThisMonth;
        public int LastMonthStarted;
        public double Delivered;
        public double Revenue;
        public int ShortMonths;
        /// <summary>Average share of the price paid after the yield clause (1 = full price).</summary>
        public double QualityPaid = 1;
        public bool Allocated;
    }

    public enum AsicStatus { Offered, Accepted, Supplying, Done, Failed, Declined }

    /// <summary>A custom chip a client wants: you design it to their spec for an engineering fee, then supply it.</summary>
    public class AsicDeal
    {
        public string Id;
        public string ClientId;
        public string SegmentId;
        public ChipSpec Spec;
        public double Nre;
        public double NrePaid;
        public double UnitPrice;
        public double MonthlyUnits;
        public int LengthMonths;
        public int OfferedMonth;
        public int ExpiresMonth;
        /// <summary>The chip must be released, meeting the spec, by the end of this month.</summary>
        public int DeadlineMonth;
        public AsicStatus Status;
        public string DesignId;
        public string ContractId;
        public bool WarnedSpec;
    }
}
