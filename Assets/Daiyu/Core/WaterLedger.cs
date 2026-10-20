using System;

namespace Daiyu.Core
{
    /// <summary>Serializable ledger state. Account ordering is part of the save contract.</summary>
    [Serializable]
    public sealed class WaterLedgerPoolSnapshot
    {
        public long[] amounts;
    }

    [Serializable]
    public sealed class WaterLedgerSnapshot
    {
        public int version;
        public WaterLedgerPoolSnapshot[] pools;
        public long initialTotal;
        public long transferCount;
        public long divineRainTransferred;
    }

    // Controlled is a separately addressed part of the surface-water category.
    // Reserve is retained only as an empty, non-reorderable compatibility slot; no water is ever
    // held there anymore. Atmosphere is the universal destination for evaporation/transpiration.
    // Groundwater now holds a variable number of per-node accounts (WO-A2a): index 0..N-1 of the
    // Groundwater pool are the catalog's fixed-topology nodes/confluence points, allocated via
    // AllocateGroundwater. The pool starts at whatever size the constructor is given (historically a
    // single scalar) and only grows through AllocateGroundwater, never shrinks or reorders.
    // Ocean (WO-A3) is appended at the end of the enum to preserve every existing ordinal: it holds
    // the sea's pooled water inventory. Like Groundwater's node pool, it starts empty (size 0 unless
    // constructed with an explicit account count) and only grows via AllocateOcean, which — like
    // AllocateGroundwater — both allocates storage AND funds it with an initial stock in one call.
    public enum WaterStore { Surface, SoilLife, Groundwater, Natural, Reserve, Atmosphere, Controlled, River, Source, RiverNode, SurfaceTransit, Ocean }
    public enum TransferReason { Flow, DivineDrag, Evaporation, DivineRain, NaturalRain, Infiltration, Pump, Gather, Release, CloudRain }

    public readonly struct WaterAddress : IEquatable<WaterAddress>
    {
        public readonly WaterStore Store;
        public readonly int Index;
        public WaterAddress(WaterStore store, int index) { Store = store; Index = index; }
        public bool Equals(WaterAddress other)=>Store==other.Store&&Index==other.Index;
        public override bool Equals(object value)=>value is WaterAddress other&&Equals(other);
        public override int GetHashCode()=>unchecked((int)Store*397^Index);
    }

    public readonly struct WaterTransfer
    {
        public readonly WaterAddress Source, Destination;
        public readonly long Amount;
        public readonly TransferReason Reason;
        public WaterTransfer(WaterAddress source, WaterAddress destination, long amount, TransferReason reason)
        { Source = source; Destination = destination; Amount = amount; Reason = reason; }
    }

    /// <summary>Mutually exclusive world categories, in integer inventory units. Surface includes
    /// cells, carried water, channels, nodes and finite source; subaccounts are counted once.
    /// Groundwater sums every node/confluence account (fixed-topology network plus any legacy scalar
    /// slot). Ocean sums every pooled sea account (WO-A3; typically a single account). Reserve is an
    /// inert compatibility slot and is not an active resource category; its amount is always 0 and is
    /// omitted from Total/Totals.</summary>
    public readonly struct WaterTotals
    {
        public readonly long Surface,SoilLife,Groundwater,Natural,Atmosphere,Ocean,InitialTotal;
        public long Total=>checked(Surface+SoilLife+Groundwater+Natural+Atmosphere+Ocean);
        internal WaterTotals(long surface,long soilLife,long groundwater,long natural,long atmosphere,long ocean,long initialTotal)
        {Surface=surface;SoilLife=soilLife;Groundwater=groundwater;Natural=natural;Atmosphere=atmosphere;Ocean=ocean;InitialTotal=initialTotal;}
    }

    /// <summary>Integer inventory. After construction, Transfer is the only mutation path for moving
    /// water between existing accounts; AllocateGroundwater/AllocateOcean/AllocateTransit are the only
    /// ways to add new accounts (and, for the first two, new water) during world setup.</summary>
    public sealed class WaterLedger
    {
        public const int UnitsPerWater = 1000;
        readonly long[][] pools;
        /// <summary>The conservation baseline that every later Total()/Totals.Total comparison is
        /// checked against. This starts as the constructor's total, but setup is not necessarily
        /// finished at construction time: EnableGroundwater/EnableOcean (and anything else that calls
        /// AllocateGroundwater/AllocateOcean post-construction) inject additional, newly-created water
        /// into the ledger *after* the constructor has already run and *after* InitialTotal was first
        /// computed. AllocateGroundwater/AllocateOcean therefore advance InitialTotal by exactly the
        /// amount of water they add, so that "conserved" always means "conserved relative to the total
        /// that existed once world setup actually finished" — not a stale pre-setup snapshot that every
        /// subsequent Total() comparison would then spuriously fail against for the rest of the run.</summary>
        public long InitialTotal { get; private set; }
        public WaterTransfer LastTransfer { get; private set; }
        public long TransferCount { get; private set; }
        public long DivineRainTransferred {get;private set;}
        long StoreTotal(WaterStore store) {long sum=0;foreach(long amount in pools[(int)store])sum=checked(sum+amount);return sum;}
        public WaterTotals Totals=>new WaterTotals(checked(StoreTotal(WaterStore.Surface)+StoreTotal(WaterStore.Controlled)+StoreTotal(WaterStore.River)+StoreTotal(WaterStore.Source)+StoreTotal(WaterStore.RiverNode)+StoreTotal(WaterStore.SurfaceTransit)),StoreTotal(WaterStore.SoilLife),StoreTotal(WaterStore.Groundwater),StoreTotal(WaterStore.Natural),StoreTotal(WaterStore.Atmosphere),StoreTotal(WaterStore.Ocean),InitialTotal);
        public int StoreCount(WaterStore store) {int index=(int)store;if(index<0||index>=pools.Length)throw new ArgumentException("Invalid water store.");return pools[index].Length;}
        public long Total
        {
            get { long total = 0; foreach (var pool in pools) foreach (long amount in pool) total = checked(total + amount); return total; }
        }

        public WaterLedgerSnapshot CaptureSnapshot()
        {
            var copy=new WaterLedgerPoolSnapshot[pools.Length];
            for(int i=0;i<pools.Length;i++)copy[i]=new WaterLedgerPoolSnapshot{amounts=(long[])pools[i].Clone()};
            return new WaterLedgerSnapshot{version=1,pools=copy,initialTotal=InitialTotal,
                transferCount=TransferCount,divineRainTransferred=DivineRainTransferred};
        }

        public void RestoreSnapshot(WaterLedgerSnapshot snapshot)
        {
            if(snapshot==null||snapshot.version!=1||snapshot.pools==null||snapshot.pools.Length!=pools.Length)
                throw new ArgumentException("Unsupported water ledger snapshot.");
            long total=0;
            for(int i=0;i<pools.Length;i++)
            {
                var source=snapshot.pools[i]?.amounts;if(source==null||source.Length!=pools[i].Length)throw new ArgumentException("Water ledger account layout differs.");
                for(int j=0;j<source.Length;j++){if(source[j]<0)throw new ArgumentException("Negative saved water.");total=checked(total+source[j]);}
            }
            if(snapshot.initialTotal<0||snapshot.transferCount<0||snapshot.divineRainTransferred<0||total!=snapshot.initialTotal)
                throw new ArgumentException("Invalid water ledger conservation baseline.");
            for(int i=0;i<pools.Length;i++)Array.Copy(snapshot.pools[i].amounts,pools[i],pools[i].Length);
            InitialTotal=snapshot.initialTotal;TransferCount=snapshot.transferCount;DivineRainTransferred=snapshot.divineRainTransferred;
            LastTransfer=default(WaterTransfer);
        }

        public WaterLedger(long[] surface, long groundwater = 0, long natural = 0)
            : this(surface, Array.Empty<long>(), 0, groundwater, natural) { }

        public WaterLedger(long[] surface, long[] river, long source, long groundwater = 0, long natural = 0)
            : this(surface, river, Array.Empty<long>(), source, groundwater, natural) { }

        public WaterLedger(long[] surface, long[] river, long[] riverNodes, long source, long groundwater = 0, long natural = 0)
        {
            if (surface == null || surface.Length == 0) throw new ArgumentException("Surface cells required.");
            if (river == null) throw new ArgumentException("River inventory required.");
            if (riverNodes == null) throw new ArgumentException("Node inventory required.");
            // Reserve pool is a single always-empty slot: original enum index retained for compatibility.
            // Ocean pool starts empty (size 0): WO-A3 worlds call AllocateOcean after construction, exactly
            // like AllocateGroundwater for the groundwater node pool.
            pools = new[] { (long[])surface.Clone(), new long[surface.Length], new[] { groundwater },
                new[] { natural }, new long[1], new long[surface.Length], new long[1], (long[])river.Clone(), new[] { source }, (long[])riverNodes.Clone(), Array.Empty<long>(), Array.Empty<long>() };
            foreach (var pool in pools) foreach (long amount in pool) if (amount < 0) throw new ArgumentException("Negative initial water.");
            InitialTotal = Total;
        }

        /// <summary>Append empty, stable transit addresses. This allocates storage, never water.</summary>
        public int AllocateTransit(int count)
        {
            if(count<=0||count>10000)throw new ArgumentException("Invalid transit segment count.");
            int store=(int)WaterStore.SurfaceTransit,start=pools[store].Length;
            var expanded=new long[checked(start+count)];Array.Copy(pools[store],expanded,start);pools[store]=expanded;return start;
        }

        /// <summary>Append new, pre-funded groundwater node accounts (WO-A2a). Unlike AllocateTransit,
        /// this allocates both storage and initial water: each value in initialStocks becomes the
        /// starting balance of a new Groundwater index, and the ledger's total grows by their sum.
        /// This is the only way new groundwater water enters the world after construction, and it must
        /// only be called once per world during setup (before any Transfer against these indices).
        /// InitialTotal is advanced by exactly the sum credited here, so the conservation invariant for
        /// the rest of the run is checked against the total that exists once this allocation has
        /// happened — not the pre-setup constructor snapshot. Returns the starting index of the newly
        /// appended accounts.</summary>
        public int AllocateGroundwater(long[] initialStocks)
        {
            if(initialStocks==null||initialStocks.Length==0||initialStocks.Length>100000)throw new ArgumentException("Invalid groundwater node count.");
            foreach(long v in initialStocks)if(v<0)throw new ArgumentException("Negative initial groundwater stock.");
            long added=0;foreach(long v in initialStocks)added=checked(added+v);
            long total=checked(InitialTotal+added);
            int store=(int)WaterStore.Groundwater,start=pools[store].Length;
            var expanded=new long[checked(start+initialStocks.Length)];
            Array.Copy(pools[store],expanded,start);Array.Copy(initialStocks,0,expanded,start,initialStocks.Length);
            pools[store]=expanded;
            InitialTotal=total;
            return start;
        }

        /// <summary>Append new, pre-funded ocean accounts (WO-A3). Same pattern as AllocateGroundwater:
        /// allocates storage AND funds it in one call, growing the ledger's total by the sum of
        /// initialStocks, and advancing InitialTotal by that same sum so conservation checks made after
        /// setup compare against the fully-funded baseline rather than a stale pre-allocation total.
        /// Must only be called during setup, before any Transfer against these indices. Returns the
        /// starting index of the newly appended accounts (typically 0, for the simple
        /// single-pool-ocean design — see NOTES).</summary>
        public int AllocateOcean(long[] initialStocks)
        {
            if(initialStocks==null||initialStocks.Length==0||initialStocks.Length>100000)throw new ArgumentException("Invalid ocean account count.");
            foreach(long v in initialStocks)if(v<0)throw new ArgumentException("Negative initial ocean stock.");
            long added=0;foreach(long v in initialStocks)added=checked(added+v);
            long total=checked(InitialTotal+added);
            int store=(int)WaterStore.Ocean,start=pools[store].Length;
            var expanded=new long[checked(start+initialStocks.Length)];
            Array.Copy(pools[store],expanded,start);Array.Copy(initialStocks,0,expanded,start,initialStocks.Length);
            pools[store]=expanded;
            InitialTotal=total;
            return start;
        }

        public int AllocateGroundwater(int count)
        {
            if(count<=0||count>10000)throw new ArgumentException("Invalid groundwater count.");
            return AllocateGroundwater(new long[count]);
        }

        void Validate(WaterAddress address)
        {
            int store = (int)address.Store;
            if (store < 0 || store >= pools.Length || address.Index < 0 || address.Index >= pools[store].Length)
                throw new ArgumentException("Invalid water address.");
        }

        public long Read(WaterAddress address) { Validate(address); return pools[(int)address.Store][address.Index]; }

        /// <summary>The only mutation path. A zero-quantity or same-address call is a harmless no-op
        /// (returns a zero-amount transfer) rather than an error, so that normal zero-cloud
        /// summon-rain and other legitimately-empty requests never throw. Only a negative quantity
        /// or an address that fails Validate is rejected.</summary>
        public WaterTransfer Transfer(WaterAddress source, WaterAddress destination, long requested, TransferReason reason)
        {
            Validate(source); Validate(destination);
            if (requested < 0) throw new ArgumentException("Transfer requires a nonnegative quantity.");
            if (requested == 0 || (source.Store == destination.Store && source.Index == destination.Index))
                return new WaterTransfer(source, destination, 0, reason);
            long amount = Math.Min(requested, pools[(int)source.Store][source.Index]);
            long target = checked(pools[(int)destination.Store][destination.Index] + amount);
            long rainTotal=reason==TransferReason.DivineRain?checked(DivineRainTransferred+amount):DivineRainTransferred;
            pools[(int)source.Store][source.Index] -= amount;
            pools[(int)destination.Store][destination.Index] = target;
            var result = new WaterTransfer(source, destination, amount, reason);
            if (amount > 0) { LastTransfer = result; TransferCount++;DivineRainTransferred=rainTotal; }
            return result;
        }
    }
}
