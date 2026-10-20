using System;
using NUnit.Framework;

namespace Daiyu.Simulation.Tests
{
    public sealed class CoarseWorldTests
    {
        static CoarseNode[] Nodes() => new[] {
            new CoarseNode(0, 0, 0, 10000, 3000),
            new CoarseNode(1, 1, 0, 10000, 2000),
            new CoarseNode(2, 0, 1, 10000, 1000)
        };

        static CoarseEdge Edge(int id, int from, int to, bool enabled = true) => new CoarseEdge(id, from, to, 1000, enabled);

        static void AssertInventory(CoarseWorld world)
        {
            Assert.That(world.TotalWater, Is.EqualTo(world.InitialWater));
            for (int i = 0; i < world.NodeCount; i++)
                Assert.That(world.WaterAt(i), Is.InRange(0L, world.Node(i).Capacity));
        }

        [TestCase(0u)]
        [TestCase(1234u)]
        [TestCase(uint.MaxValue)]
        public void GeneratedMapsAreSeededAndConnectedDownhill(uint seed)
        {
            var a = CoarseWorld.Generate(seed, 8);
            var b = CoarseWorld.Generate(seed, 8);
            Assert.That(a.NodeCount, Is.EqualTo(217));
            Assert.That(a.TotalWater, Is.EqualTo(7000));
            int source = -1;
            for (int i = 0; i < a.NodeCount; i++)
            {
                Assert.That(a.Node(i).Height, Is.EqualTo(b.Node(i).Height));
                if (a.WaterAt(i) > 0) source = i;
            }
            int count = 0;
            for (int i = 0; i < a.EdgeCount; i++)
            {
                var e = a.Edge(i);
                Assert.That(a.Node(e.From).Height, Is.GreaterThan(a.Node(e.To).Height));
                Assert.That(e.Enabled, Is.False);
                if (e.From == source)
                {
                    Assert.That(a.TryPlan(e.From, e.To, out int planned), Is.True);
                    Assert.That(a.CommitPlan(planned), Is.True);
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0));
            Assert.That(a.TotalWater, Is.EqualTo(7000));
            a.TickOnce();
            Assert.That(a.WaterAt(source), Is.LessThan(7000));
            for (int i = 0; i < 100; i++) a.TickOnce();
            AssertInventory(a);
        }

        [Test]
        public void DifferentSeedsChangeTerrain()
        {
            var a = CoarseWorld.Generate(12);
            var b = CoarseWorld.Generate(34);
            bool different = false;
            for (int i = 0; i < a.NodeCount; i++) different |= a.Node(i).Height != b.Node(i).Height;
            Assert.That(different, Is.True);
        }

        [Test]
        public void PlanningAndCommitOnlyEnableConnections()
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1, false), Edge(1, 1, 2, false) }, new long[] { 7000, 0, 0 });
            Assert.That(world.TryPlan(0, 1, out int edge), Is.True);
            Assert.That(edge, Is.EqualTo(0));
            Assert.That(world.TryPlan(1, 2, out _), Is.False);
            Assert.That(world.CommitPath(new[] { 0, 1, 2 }), Is.True);
            Assert.That(world.WaterAt(0), Is.EqualTo(7000));
            Assert.That(world.WaterAt(1), Is.Zero);
            Assert.That(world.WaterAt(2), Is.Zero);
            Assert.That(world.Tick, Is.Zero);
            Assert.That(world.CommitPath(new[] { 0, 1, 2 }), Is.False);
            AssertInventory(world);
        }

        [Test]
        public void PathPlanningIsReadOnlyAndCommitEnablesWholePath()
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1, false), Edge(1, 1, 2, false) }, new long[] { 7000, 0, 0 });
            Assert.That(world.TryFindPath(0, 2, out int[] path), Is.True);
            Assert.That(path, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(world.Edge(0).Enabled, Is.False);
            Assert.That(world.Edge(1).Enabled, Is.False);
            Assert.That(world.CommitPath(path), Is.True);
            Assert.That(world.Edge(0).Enabled, Is.True);
            Assert.That(world.Edge(1).Enabled, Is.True);
            Assert.That(world.TotalWater, Is.EqualTo(7000));
        }

        [Test]
        public void InvalidPathIsAtomicAndDryStartCannotCommit()
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1, false), Edge(1, 1, 2, false) }, new long[] { 7000, 0, 0 });
            Assert.That(world.CommitPath(new[] { 0, 1, 0 }), Is.False);
            Assert.That(world.CommitPath(new[] { 0, 1, 99 }), Is.False);
            Assert.That(world.CommitPath(new[] { 0, 2 }), Is.False);
            Assert.That(world.CommitPath(new[] { 1, 2 }), Is.False);
            Assert.That(world.CommitPlan(1), Is.False);
            Assert.That(world.Edge(0).Enabled, Is.False);
            Assert.That(world.Edge(1).Enabled, Is.False);
        }

        [Test]
        public void ClosedConnectionsDoNotFlow()
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1, false) }, new long[] { 7000, 0, 0 });
            world.TickOnce();
            Assert.That(world.WaterAt(0), Is.EqualTo(7000));
            Assert.That(world.LastFlow(0), Is.Zero);
        }

        [Test]
        public void BranchesShareOldSourceBudget()
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1), Edge(1, 0, 2) }, new long[] { 500, 0, 0 });
            world.TickOnce();
            Assert.That(world.WaterAt(0), Is.Zero);
            Assert.That(world.LastFlow(0), Is.EqualTo(500));
            Assert.That(world.LastFlow(1), Is.Zero);
            AssertInventory(world);
        }

        [TestCase(9500L, 500L)]
        [TestCase(10000L, 0L)]
        public void MergingFlowsShareOldReceivingCapacity(long initial, long expected)
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 2), Edge(1, 1, 2) }, new long[] { 8000, 8000, initial });
            world.TickOnce();
            Assert.That(world.LastFlow(0) + world.LastFlow(1), Is.EqualTo(expected));
            Assert.That(world.WaterAt(2), Is.EqualTo(10000));
            AssertInventory(world);
        }

        [Test]
        public void NewReceiptCannotFlowAnotherEdgeInSameTick()
        {
            var world = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1), Edge(1, 1, 2) }, new long[] { 1000, 0, 0 });
            world.TickOnce();
            Assert.That(world.WaterAt(1), Is.EqualTo(1000));
            Assert.That(world.WaterAt(2), Is.Zero);
            world.TickOnce();
            Assert.That(world.WaterAt(2), Is.EqualTo(1000));
            AssertInventory(world);
        }

        [Test]
        public void DownhillTerrainStillRequiresPositiveWaterHead()
        {
            var nodes = Nodes();
            nodes[0] = new CoarseNode(0, 0, 0, 10000, 2001);
            var world = CoarseWorld.Create(1, nodes, new[] { Edge(0, 0, 1) }, new long[] { 500, 9000, 0 });
            world.TickOnce();
            Assert.That(world.LastFlow(0), Is.Zero);
            Assert.That(world.WaterAt(0), Is.EqualTo(500));
            AssertInventory(world);
        }

        [Test]
        public void IntegerClockRetainsRemainderAndIsChunkIndependent()
        {
            var a = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1) }, new long[] { 7000, 0, 0 });
            var b = CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1) }, new long[] { 7000, 0, 0 });
            a.Advance(1100);
            b.Advance(99); b.Advance(1); b.Advance(1000);
            Assert.That(a.Tick, Is.EqualTo(4));
            Assert.That(a.RemainderMilliseconds, Is.EqualTo(100));
            Assert.That(b.Tick, Is.EqualTo(a.Tick));
            Assert.That(b.RemainderMilliseconds, Is.EqualTo(a.RemainderMilliseconds));
            for (int i = 0; i < a.NodeCount; i++) Assert.That(b.WaterAt(i), Is.EqualTo(a.WaterAt(i)));
            Assert.Throws<ArgumentOutOfRangeException>(() => a.Advance(-1));
        }

        [Test]
        public void TopologyRejectsUphillDisconnectedAndOverfilledInputs()
        {
            Assert.Throws<ArgumentException>(() => CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 1, 0) }, new long[] { 7000, 0, 0 }));
            var nodes = Nodes(); nodes[2] = new CoarseNode(2, 4, 0, 10000, 1000);
            Assert.Throws<ArgumentException>(() => CoarseWorld.Create(1, nodes, new[] { Edge(0, 0, 2) }, new long[] { 7000, 0, 0 }));
            Assert.Throws<ArgumentException>(() => CoarseWorld.Create(1, Nodes(), new[] { Edge(0, 0, 1) }, new long[] { 10001, 0, 0 }));
        }

        [Test]
        public void ConstructionCopiesInputsAndDoesNotExposeWritableLedger()
        {
            var nodes = Nodes(); var edges = new[] { Edge(0, 0, 1) }; var water = new long[] { 7000, 0, 0 };
            var world = CoarseWorld.Create(1, nodes, edges, water);
            nodes[0] = new CoarseNode(0, 0, 0, 1, 0);
            edges[0] = Edge(0, 0, 2);
            water[0] = 0;
            Assert.That(world.Node(0).Capacity, Is.EqualTo(10000));
            Assert.That(world.Edge(0).To, Is.EqualTo(1));
            Assert.That(world.WaterAt(0), Is.EqualTo(7000));
            foreach (var property in typeof(CoarseWorld).GetProperties())
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(Daiyu.Core.WaterLedger)));
        }

        [Test]
        public void SnapshotRoundTripContinuesIdenticallyWithoutAliasing()
        {
            var original = CoarseWorld.Create(1234, Nodes(), new[] { Edge(0, 0, 1), Edge(1, 1, 2) }, new long[] { 7000, 0, 0 });
            original.Advance(825);
            var snapshot = original.CaptureSnapshot();
            var restored = CoarseWorld.Restore(snapshot);
            snapshot.nodes[0].height = 0;
            snapshot.ledger.pools[0].amounts[0] = 0;
            Assert.That(restored.Node(0).Height, Is.EqualTo(original.Node(0).Height));
            Assert.That(restored.WaterAt(0), Is.EqualTo(original.WaterAt(0)));
            original.Advance(1375); restored.Advance(1375);
            Assert.That(restored.Seed, Is.EqualTo(original.Seed));
            Assert.That(restored.Tick, Is.EqualTo(original.Tick));
            Assert.That(restored.RemainderMilliseconds, Is.EqualTo(original.RemainderMilliseconds));
            for (int i = 0; i < original.NodeCount; i++) Assert.That(restored.WaterAt(i), Is.EqualTo(original.WaterAt(i)));
            AssertInventory(restored);
        }

        [Test]
        public void InvalidCandidateDoesNotChangeOriginalWorld()
        {
            var world = CoarseWorld.Generate(1234);
            var bad = world.CaptureSnapshot();
            bad.ledger.initialTotal++;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(bad));
            bad = world.CaptureSnapshot(); bad.remainderMilliseconds = 250;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(bad));
            bad = world.CaptureSnapshot(); bad.modelVersion++;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(bad));
            bad = world.CaptureSnapshot(); bad.nodes[0] = null;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(bad));
            Assert.That(world.Tick, Is.Zero);
            Assert.That(world.TotalWater, Is.EqualTo(7000));
        }

        [Test]
        public void RestoreRejectsOverflowCapacityAndUnexpectedSubsystemWater()
        {
            var world = CoarseWorld.Generate(1234);
            var bad = world.CaptureSnapshot();
            int wet = -1;
            for (int i = 0; i < world.NodeCount; i++) if (world.WaterAt(i) > 0) wet = i;
            bad.nodes[wet].capacity = 100;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(bad));
            bad = world.CaptureSnapshot();
            bad.ledger.pools[(int)Daiyu.Core.WaterStore.Groundwater].amounts[wet] = CoarseWorld.GroundwaterCapacity + 1;
            bad.ledger.pools[(int)Daiyu.Core.WaterStore.Surface].amounts[wet] -= CoarseWorld.GroundwaterCapacity + 1;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(bad));
        }

        [Test]
        public void ClimateUsesLedgerForRainGroundwaterEcologyAndVisibility()
        {
            var world = CoarseWorld.Generate(1234, 2);
            int source = -1;
            for (int i = 0; i < world.NodeCount; i++) if (world.WaterAt(i) > 0) source = i;
            Assert.That(world.GroundwaterAt(source), Is.Zero);
            Assert.That(world.VisibilityAt(source), Is.GreaterThan(0));

            world.Advance(CoarseWorld.TickMilliseconds * CoarseWorld.ClimateIntervalTicks);

            Assert.That(world.Weather, Is.EqualTo(CoarseWeather.Cloudy));
            Assert.That(world.AtmosphereAt(source), Is.GreaterThan(0));
            Assert.That(world.SoilLifeAt(source), Is.GreaterThan(0));
            Assert.That(world.GroundwaterAt(source), Is.GreaterThan(0));
            int ecologyBefore = world.EcologyAt(source);
            long atmosphereBefore = world.AtmosphereAt(source);
            Assert.That(world.TrySummonRain(source, 20), Is.True);
            Assert.That(world.AtmosphereAt(source), Is.LessThan(atmosphereBefore));
            Assert.That(world.EcologyAt(source), Is.GreaterThan(ecologyBefore));
            Assert.That(world.VisibilityAt(source), Is.GreaterThan(0));
            Assert.That(world.TotalWater, Is.EqualTo(world.InitialWater));
            var restored = CoarseWorld.Restore(world.CaptureSnapshot());
            Assert.That(restored.EcologyAt(source), Is.EqualTo(world.EcologyAt(source)));
            Assert.That(restored.GroundwaterAt(source), Is.EqualTo(world.GroundwaterAt(source)));
            world.Advance(CoarseWorld.TickMilliseconds * CoarseWorld.ClimateIntervalTicks);
            restored.Advance(CoarseWorld.TickMilliseconds * CoarseWorld.ClimateIntervalTicks);
            Assert.That(restored.Tick, Is.EqualTo(world.Tick));
            Assert.That(restored.TotalWater, Is.EqualTo(world.TotalWater));
            Assert.That(restored.EcologyAt(source), Is.EqualTo(world.EcologyAt(source)));
        }

        [Test]
        public void GroundwaterSpringsReturnWaterToSurfaceWithoutCreatingInventory()
        {
            var world = CoarseWorld.Generate(1234, 2);
            int source = -1;
            for (int i = 0; i < world.NodeCount; i++) if (world.WaterAt(i) > 0) source = i;

            world.Advance(CoarseWorld.TickMilliseconds * CoarseWorld.ClimateIntervalTicks * 3);

            Assert.That(world.LastSpringFlowAt(source), Is.EqualTo(CoarseWorld.GroundwaterSpringRate));
            Assert.That(world.GroundwaterAt(source), Is.GreaterThanOrEqualTo(CoarseWorld.GroundwaterSpringThreshold));
            Assert.That(world.TotalWater, Is.EqualTo(world.InitialWater));
        }

        [Test]
        public void RestoreRejectsInvalidEcologyState()
        {
            var world = CoarseWorld.Generate(1234, 2);
            var snapshot = world.CaptureSnapshot();
            snapshot.ecology[0] = 1001;
            Assert.Throws<ArgumentException>(() => CoarseWorld.Restore(snapshot));
        }
    }
}
