using System;
using Daiyu.Core;

namespace Daiyu.Simulation
{
    [Serializable]
    public sealed class CoarseNodeSnapshot
    {
        public int q;
        public int r;
        public int height;
        public long capacity;
    }

    [Serializable]
    public sealed class CoarseEdgeSnapshot
    {
        public int from;
        public int to;
        public long rate;
        public bool enabled;
    }

    /// <summary>New-project model state, not the reference game's v4 file format.
    /// Topology is explicit so restoration never silently regenerates a different map.
    /// LastFlow and all scratch arrays are disposable presentation/simulation caches.</summary>
    [Serializable]
    public sealed class CoarseWorldSnapshot
    {
        public int formatVersion;
        public int modelVersion;
        public int generatorVersion;
        public uint seed;
        public int tickMilliseconds;
        public int unitsPerMillimeter;
        public long tick;
        public int remainderMilliseconds;
        public CoarseNodeSnapshot[] nodes;
        public CoarseEdgeSnapshot[] edges;
        public WaterLedgerSnapshot ledger;
        public int[] ecology;
    }
}
