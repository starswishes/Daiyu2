using System;
using System.Collections.Generic;
using Daiyu.Core;

namespace Daiyu.Simulation
{
    public enum CoarseWeather
    {
        Clear,
        Cloudy,
        Rain
    }

    public readonly struct CoarseNode
    {
        public readonly int Id;
        public readonly int Q;
        public readonly int R;
        public readonly long Capacity;
        public readonly int Height;

        public CoarseNode(int id, int q, int r, long capacity, int height)
        {
            Id = id; Q = q; R = r; Capacity = capacity; Height = height;
        }
    }

    public readonly struct CoarseEdge
    {
        public readonly int Id;
        public readonly int From;
        public readonly int To;
        public readonly long Rate;
        public readonly bool Enabled;

        public CoarseEdge(int id, int from, int to, long rate, bool enabled)
        {
            Id = id; From = from; To = to; Rate = rate; Enabled = enabled;
        }

        public CoarseEdge Enable() => new CoarseEdge(Id, From, To, Rate, true);
    }

    public sealed class CoarseWorld
    {
        public const int ModelVersion = 2;
        public const int GeneratorVersion = 1;
        public const int TickMilliseconds = 250;
        public const int UnitsPerMillimeter = 10;
        public const int ClimateIntervalTicks = 4;
        public const long GroundwaterCapacity = 2000;
        public const long GroundwaterSpringThreshold = 24;
        public const long GroundwaterSpringRate = 8;
        public const long SoilLifeCapacity = 10000;
        readonly CoarseNode[] nodes;
        readonly CoarseEdge[] edges;
        readonly WaterLedger ledger;
        readonly Dictionary<(int, int), int> edgeByPair;
        readonly long[] oldWater;
        readonly long[] outgoing;
        readonly long[] incoming;
        readonly long[] requests;
        readonly long[] lastFlow;
        readonly long[] lastGroundwaterRelease;
        readonly int[] ecology;
        long tick;
        int remainderMilliseconds;

        public uint Seed { get; }
        public long Tick => tick;
        public int NodeCount => nodes.Length;
        public int EdgeCount => edges.Length;
        public long TotalWater => ledger.Total;
        public long InitialWater => ledger.InitialTotal;
        public int RemainderMilliseconds => remainderMilliseconds;
        public CoarseWeather Weather => (CoarseWeather)((tick / ClimateIntervalTicks) % 3);
        public int CloudCover => Weather == CoarseWeather.Clear ? 150 : Weather == CoarseWeather.Cloudy ? 650 : 900;

        CoarseWorld(uint seed, CoarseNode[] nodes, CoarseEdge[] edges, long[] initialWater)
        {
            Seed = seed;
            this.nodes = (CoarseNode[])nodes.Clone();
            this.edges = (CoarseEdge[])edges.Clone();
            ledger = new WaterLedger(initialWater);
            oldWater = new long[nodes.Length];
            outgoing = new long[nodes.Length];
            incoming = new long[nodes.Length];
            requests = new long[edges.Length];
            lastFlow = new long[edges.Length];
            lastGroundwaterRelease = new long[nodes.Length];
            ecology = new int[nodes.Length];
            for (int i = 0; i < ecology.Length; i++) ecology[i] = 500;
            ledger.AllocateGroundwater(nodes.Length);
            edgeByPair = new Dictionary<(int, int), int>();
            for (int i = 0; i < edges.Length; i++) edgeByPair.Add((edges[i].From, edges[i].To), i);
        }

        /// <summary>Validated static topology for generation and isolated fixtures; inputs are copied.
        /// Height is millimeters, inventory is integer ledger units, Rate is units per 250ms tick.
        /// Connections are directed downhill; the old water head must also exceed the receiving head.</summary>
        public static CoarseWorld Create(uint seed, CoarseNode[] nodes, CoarseEdge[] edges, long[] initialWater)
        {
            if (nodes == null || nodes.Length == 0 || nodes.Length > 10000 || edges == null || edges.Length > 60000 ||
                initialWater == null || initialWater.Length != nodes.Length)
                throw new ArgumentException("Invalid coarse topology layout.");
            var coordinates = new HashSet<(int, int)>();
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = nodes[i];
                if (n.Id != i || n.Capacity <= 0 || n.Capacity > 1000000000 ||
                    Math.Abs((long)n.Height) > 1000000 || !coordinates.Add((n.Q, n.R)) ||
                    initialWater[i] < 0 || initialWater[i] > n.Capacity)
                    throw new ArgumentException("Invalid coarse node or inventory.");
            }
            for (int i = 0; i < edges.Length; i++)
            {
                var e = edges[i];
                if (e.Id != i || e.From < 0 || e.From >= nodes.Length || e.To < 0 || e.To >= nodes.Length ||
                    e.From == e.To || e.Rate <= 0 || e.Rate > 1000000000 || !pairs.Add((e.From, e.To)))
                    throw new ArgumentException("Invalid coarse edge.");
                var a = nodes[e.From]; var b = nodes[e.To];
                long dq = (long)a.Q - b.Q, dr = (long)a.R - b.R;
                if ((Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2 != 1 || a.Height <= b.Height)
                    throw new ArgumentException("Connections require adjacent descending nodes.");
            }
            return new CoarseWorld(seed, nodes, edges, initialWater);
        }

        public static CoarseWorld Generate(uint seed, int radius = 3)
        {
            if (radius < 1 || radius > 8) throw new ArgumentOutOfRangeException(nameof(radius));
            var nodeList = new List<CoarseNode>();
            var ids = new Dictionary<(int, int), int>();
            for (int r = -radius; r <= radius; r++)
                for (int q = -radius; q <= radius; q++)
                {
                    if (Math.Abs(q + r) > radius) continue;
                    int id = nodeList.Count;
                    ids[(q, r)] = id;
                    nodeList.Add(new CoarseNode(id, q, r, 10000, Height(seed, q, r)));
                }

            var edges = new List<CoarseEdge>();
            var water = new long[nodeList.Count];
            int source = FindHighest(nodeList);
            water[source] = 7000;
            int[] dq = { 1, 1, 0, -1, -1, 0 };
            int[] dr = { 0, -1, -1, 0, 1, 1 };
            for (int i = 0; i < nodeList.Count; i++)
                for (int d = 0; d < dq.Length; d++)
                {
                    var from = nodeList[i];
                    if (!ids.TryGetValue((from.Q + dq[d], from.R + dr[d]), out int target)) continue;
                    var to = nodeList[target];
                    if (from.Height <= to.Height) continue;
                    edges.Add(new CoarseEdge(edges.Count, from.Id, to.Id, 1000, false));
                }
            return Create(seed, nodeList.ToArray(), edges.ToArray(), water);
        }

        static int Height(uint seed, int q, int r)
        {
            unchecked
            {
                // Seeded broad continuous hills, not independent random cell heights.
                // Mixer extracted from the reference ProceduralMapGenerator; new generator version.
                uint value = seed;
                value ^= value >> 16; value *= 0x7feb352d; value ^= value >> 15;
                double phase = value / (double)uint.MaxValue * Math.PI * 2;
                double x = q + r * .5, z = r * .866025403784;
                return (int)Math.Round(2500 - z * 120 + 400 * Math.Sin(x * .38 + phase) * Math.Cos(z * .29 - phase));
            }
        }

        static int FindHighest(IReadOnlyList<CoarseNode> values)
        {
            int result = 0;
            for (int i = 1; i < values.Count; i++) if (values[i].Height > values[result].Height) result = i;
            return result;
        }

        public CoarseNode Node(int id) => nodes[CheckedNode(id)];
        public CoarseEdge Edge(int id) => edges[CheckedEdge(id)];
        public long WaterAt(int node) => ledger.Read(new WaterAddress(WaterStore.Surface, CheckedNode(node)));
        public long SoilLifeAt(int node) => ledger.Read(new WaterAddress(WaterStore.SoilLife, CheckedNode(node)));
        public long GroundwaterAt(int node) => ledger.Read(new WaterAddress(WaterStore.Groundwater, CheckedNode(node)));
        public long AtmosphereAt(int node) => ledger.Read(new WaterAddress(WaterStore.Atmosphere, CheckedNode(node)));
        public long LastSpringFlowAt(int node) => lastGroundwaterRelease[CheckedNode(node)];
        public int EcologyAt(int node) => ecology[CheckedNode(node)];

        public int VisibilityAt(int node)
        {
            int index = CheckedNode(node);
            long moisture = SoilLifeAt(index) + GroundwaterAt(index);
            int visibility = 250 + ecology[index] / 2 + (WaterAt(index) > 0 ? 200 : 0) + (moisture > 0 ? 100 : 0);
            if (Weather == CoarseWeather.Rain) visibility -= 150;
            return Math.Max(0, Math.Min(1000, visibility));
        }

        public bool TryPlan(int from, int to, out int edgeId)
        {
            from = CheckedNode(from); to = CheckedNode(to); edgeId = -1;
            if (!edgeByPair.TryGetValue((from, to), out int candidate) || WaterAt(from) == 0) return false;
            edgeId = candidate;
            return true;
        }

        /// <summary>Finds a deterministic directed route through static downhill edges without changing state.</summary>
        public bool TryFindPath(int from, int to, out int[] path)
        {
            from = CheckedNode(from); to = CheckedNode(to);
            path = null;
            if (from == to || WaterAt(from) == 0) return false;
            var previous = new int[nodes.Length];
            for (int i = 0; i < previous.Length; i++) previous[i] = -1;
            var queue = new Queue<int>(); queue.Enqueue(from); previous[from] = from;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                for (int i = 0; i < edges.Length; i++)
                {
                    if (edges[i].From != current) continue;
                    int next = edges[i].To;
                    if (previous[next] >= 0) continue;
                    previous[next] = current;
                    if (next == to) { queue.Clear(); break; }
                    queue.Enqueue(next);
                }
            }
            if (previous[to] < 0) return false;
            var reverse = new List<int>();
            for (int current = to; current != from; current = previous[current]) reverse.Add(current);
            reverse.Add(from); reverse.Reverse(); path = reverse.ToArray();
            return true;
        }

        public bool CommitPlan(int edgeId)
        {
            int index = CheckedEdge(edgeId);
            if (edges[index].Enabled || WaterAt(edges[index].From) == 0) return false;
            edges[index] = edges[index].Enable();
            return true;
        }

        /// <summary>Atomically enable a continuous route. Only the start must already be wet.</summary>
        public bool CommitPath(IReadOnlyList<int> path)
        {
            if (path == null || path.Count < 2 || path.Count > nodes.Length) return false;
            var visited = new HashSet<int>();
            var selected = new int[path.Count - 1];
            for (int i = 0; i < path.Count; i++)
            {
                int node = path[i];
                if (node < 0 || node >= nodes.Length || !visited.Add(node)) return false;
                if (i > 0 && !edgeByPair.TryGetValue((path[i - 1], node), out selected[i - 1])) return false;
            }
            if (WaterAt(path[0]) == 0) return false;
            bool changed = false;
            foreach (int e in selected) if (!edges[e].Enabled) { edges[e] = edges[e].Enable(); changed = true; }
            return changed;
        }

        /// <summary>Moves local atmospheric water onto a surface node through the same ledger used by flow.</summary>
        public bool TrySummonRain(int node, long requested = 250)
        {
            int index = CheckedNode(node);
            if (requested <= 0) return false;
            long available = AtmosphereAt(index);
            long room = nodes[index].Capacity - WaterAt(index);
            long amount = Math.Min(requested, Math.Min(available, room));
            if (amount <= 0) return false;
            bool transferred = ledger.Transfer(new WaterAddress(WaterStore.Atmosphere, index),
                new WaterAddress(WaterStore.Surface, index), amount, TransferReason.DivineRain).Amount > 0;
            if (transferred) ecology[index] = Math.Min(1000, ecology[index] + 4);
            return transferred;
        }

        public long LastFlow(int edge) => lastFlow[CheckedEdge(edge)];

        public CoarseWorldSnapshot CaptureSnapshot()
        {
            var savedNodes = new CoarseNodeSnapshot[nodes.Length];
            var savedEdges = new CoarseEdgeSnapshot[edges.Length];
            for (int i = 0; i < nodes.Length; i++)
                savedNodes[i] = new CoarseNodeSnapshot { q = nodes[i].Q, r = nodes[i].R,
                    height = nodes[i].Height, capacity = nodes[i].Capacity };
            for (int i = 0; i < edges.Length; i++)
                savedEdges[i] = new CoarseEdgeSnapshot { from = edges[i].From, to = edges[i].To,
                    rate = edges[i].Rate, enabled = edges[i].Enabled };
            return new CoarseWorldSnapshot { formatVersion = 2, modelVersion = ModelVersion,
                generatorVersion = GeneratorVersion, seed = Seed, tickMilliseconds = TickMilliseconds,
                unitsPerMillimeter = UnitsPerMillimeter, tick = tick, remainderMilliseconds = remainderMilliseconds,
                nodes = savedNodes, edges = savedEdges, ledger = ledger.CaptureSnapshot(),
                ecology = (int[])ecology.Clone() };
        }

        /// <summary>Build and validate a separate candidate. Never mutates an installed world.</summary>
        public static CoarseWorld Restore(CoarseWorldSnapshot snapshot)
        {
            if (snapshot == null || snapshot.formatVersion != 2 || snapshot.modelVersion != ModelVersion ||
                snapshot.generatorVersion != GeneratorVersion || snapshot.tickMilliseconds != TickMilliseconds ||
                snapshot.unitsPerMillimeter != UnitsPerMillimeter || snapshot.tick < 0 ||
                snapshot.remainderMilliseconds < 0 || snapshot.remainderMilliseconds >= TickMilliseconds ||
                snapshot.nodes == null || snapshot.nodes.Length == 0 || snapshot.nodes.Length > 10000 ||
                snapshot.edges == null || snapshot.edges.Length > 60000 || snapshot.ledger == null ||
                snapshot.ecology == null || snapshot.ecology.Length != snapshot.nodes.Length)
                throw new ArgumentException("Unsupported or invalid coarse world snapshot.");
            var nodes = new CoarseNode[snapshot.nodes.Length];
            var edges = new CoarseEdge[snapshot.edges.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = snapshot.nodes[i] ?? throw new ArgumentException("Missing saved node.");
                nodes[i] = new CoarseNode(i, n.q, n.r, n.capacity, n.height);
            }
            for (int i = 0; i < edges.Length; i++)
            {
                var e = snapshot.edges[i] ?? throw new ArgumentException("Missing saved edge.");
                edges[i] = new CoarseEdge(i, e.from, e.to, e.rate, e.enabled);
            }
            var candidate = Create(snapshot.seed, nodes, edges, new long[nodes.Length]);
            candidate.ledger.RestoreSnapshot(snapshot.ledger);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (candidate.WaterAt(i) > nodes[i].Capacity || candidate.SoilLifeAt(i) > SoilLifeCapacity ||
                    candidate.GroundwaterAt(i) > GroundwaterCapacity)
                    throw new ArgumentException("Saved water account exceeds capacity.");
                if (snapshot.ecology[i] < 0 || snapshot.ecology[i] > 1000)
                    throw new ArgumentException("Saved ecology is outside its valid range.");
                candidate.ecology[i] = snapshot.ecology[i];
            }
            candidate.tick = snapshot.tick;
            candidate.remainderMilliseconds = snapshot.remainderMilliseconds;
            return candidate;
        }

        /// <summary>Exact integer time, no wall-clock catch-up or discarded fractional tick.</summary>
        public void Advance(int elapsedMilliseconds)
        {
            if (elapsedMilliseconds < 0 || elapsedMilliseconds > 60000) throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
            int pending = remainderMilliseconds + elapsedMilliseconds;
            while (pending >= TickMilliseconds) { TickOnce(); pending -= TickMilliseconds; }
            remainderMilliseconds = pending;
        }

        public void TickOnce()
        {
            if (tick == long.MaxValue) throw new InvalidOperationException("Simulation clock exhausted.");
            Array.Clear(outgoing, 0, outgoing.Length);
            Array.Clear(incoming, 0, incoming.Length);
            Array.Clear(requests, 0, requests.Length);
            Array.Clear(lastFlow, 0, lastFlow.Length);
            for (int i = 0; i < nodes.Length; i++) oldWater[i] = WaterAt(i);
            foreach (var edge in edges)
            {
                if (!edge.Enabled) continue;
                long headDifference = (long)(nodes[edge.From].Height - nodes[edge.To].Height) * UnitsPerMillimeter +
                    oldWater[edge.From] - oldWater[edge.To];
                if (headDifference <= 0) continue;
                long available = oldWater[edge.From] - outgoing[edge.From];
                long room = nodes[edge.To].Capacity - oldWater[edge.To] - incoming[edge.To];
                long amount = Math.Min(headDifference / 2, Math.Min(edge.Rate, Math.Min(available, room)));
                if (amount <= 0) continue;
                requests[edge.Id] = amount;
                outgoing[edge.From] += amount;
                incoming[edge.To] += amount;
            }
            // Both budgets use the old step. New receipts cannot pass through another edge this tick.
            // Reserving old free capacity also prevents multiple upstream requests overfilling a node.
            foreach (var edge in edges)
                if (requests[edge.Id] > 0)
                    lastFlow[edge.Id] = ledger.Transfer(new WaterAddress(WaterStore.Surface, edge.From),
                        new WaterAddress(WaterStore.Surface, edge.To), requests[edge.Id], TransferReason.Flow).Amount;
            tick++;
            if (tick % ClimateIntervalTicks == 0) AdvanceClimate();
        }

        void AdvanceClimate()
        {
            CoarseWeather weather = Weather;
            for (int i = 0; i < nodes.Length; i++)
            {
                lastGroundwaterRelease[i] = 0;
                if (weather == CoarseWeather.Rain)
                {
                    long room = nodes[i].Capacity - WaterAt(i);
                    long amount = Math.Min(40, Math.Min(AtmosphereAt(i), room));
                    if (amount > 0)
                        ledger.Transfer(new WaterAddress(WaterStore.Atmosphere, i),
                            new WaterAddress(WaterStore.Surface, i), amount, TransferReason.NaturalRain);
                }
                else
                {
                    long evaporation = Math.Min(35, WaterAt(i));
                    if (evaporation > 0)
                        ledger.Transfer(new WaterAddress(WaterStore.Surface, i),
                            new WaterAddress(WaterStore.Atmosphere, i), evaporation, TransferReason.Evaporation);
                }

                long infiltration = Math.Min(25, WaterAt(i));
                long soilRoom = SoilLifeCapacity - SoilLifeAt(i);
                infiltration = Math.Min(infiltration, soilRoom);
                if (infiltration > 0)
                    ledger.Transfer(new WaterAddress(WaterStore.Surface, i),
                        new WaterAddress(WaterStore.SoilLife, i), infiltration, TransferReason.Infiltration);

                long recharge = Math.Min(12, Math.Min(SoilLifeAt(i), GroundwaterCapacity - GroundwaterAt(i)));
                if (recharge > 0)
                    ledger.Transfer(new WaterAddress(WaterStore.SoilLife, i),
                        new WaterAddress(WaterStore.Groundwater, i), recharge, TransferReason.Infiltration);

                long springRoom = nodes[i].Capacity - WaterAt(i);
                long springWater = GroundwaterAt(i) - GroundwaterSpringThreshold;
                long spring = Math.Min(GroundwaterSpringRate, Math.Min(springWater, springRoom));
                if (spring > 0)
                {
                    lastGroundwaterRelease[i] = ledger.Transfer(new WaterAddress(WaterStore.Groundwater, i),
                        new WaterAddress(WaterStore.Surface, i), spring, TransferReason.Release).Amount;
                }

                long moisture = SoilLifeAt(i) + GroundwaterAt(i);
                if (moisture >= 20) ecology[i] = Math.Min(1000, ecology[i] + 12);
                else if (WaterAt(i) == 0) ecology[i] = Math.Max(0, ecology[i] - 8);
                else ecology[i] = Math.Max(0, ecology[i] - 2);
            }
        }

        int CheckedNode(int id) { if (id < 0 || id >= nodes.Length) throw new ArgumentOutOfRangeException(nameof(id)); return id; }
        int CheckedEdge(int id) { if (id < 0 || id >= edges.Length) throw new ArgumentOutOfRangeException(nameof(id)); return id; }
    }
}
