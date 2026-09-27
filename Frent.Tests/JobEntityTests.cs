using System;
using System;
using System.Collections.Generic;
using System.Linq;
using Frent.Core;
using Frent.Jobs;
using Frent.Marshalling;
using Frent.Tests.Helpers;
using static NUnit.Framework.Assert;

namespace Frent.Tests;

internal struct JobPos
{
    public float X;
}

internal struct JobVel
{
    public float X;
}

internal struct JobTag
{
    public int Value;
}

internal struct MoveJob : IJobEntity
{
    public float Dt;
    private void Execute(ref JobPos p, in JobVel v) => p.X += v.X * Dt;
}

internal struct EntityStampJob : IJobEntity
{
    private void Execute(Entity entity, ref JobPos p) => p.X = EntityMarshal.EntityID(entity);
}

internal struct CopyJob : IJobEntity
{
    private void Execute(JobPos p, ref JobVel v) => v.X = p.X;
}

[WithAll(typeof(JobTag))]
internal struct WithAllJob : IJobEntity
{
    private void Execute(ref JobPos p) => p.X = 1;
}

[WithNone(typeof(JobTag))]
internal struct WithNoneJob : IJobEntity
{
    private void Execute(ref JobPos p) => p.X = 1;
}

internal struct MultipleExecuteJob : IJobEntity
{
    private void Execute(ref JobPos p) { }
    private void Execute(ref JobPos p, in JobVel v) { }
}

internal struct EntityNotFirstJob : IJobEntity
{
    private void Execute(ref JobPos p, Entity entity) { }
}

internal class JobEntityTests
{
    internal static World CreateWorld(int count, float vel = 1f)
    {
        var world = new World();
        for (int i = 0; i < count; i++)
            world.Create(new JobPos { X = 0 }, new JobVel { X = vel });
        return world;
    }

    internal static float SumPositions(World world)
    {
        float sum = 0;
        foreach (var (p, v) in world.Query<JobPos, JobVel>().Enumerate<JobPos, JobVel>())
            sum += p.Value.X;
        return sum;
    }

    [Test]
    public static void Run_UpdatesComponents()
    {
        using var world = CreateWorld(100);
        new MoveJob { Dt = 0.5f }.Run(world);
        That(SumPositions(world), Is.EqualTo(100 * 1f * 0.5f).Within(0.001f));
    }

    [Test]
    public static void ScheduleParallel_MatchesSerial()
    {
        using var serial = CreateWorld(50_000);
        using var parallel = CreateWorld(50_000);
        new MoveJob { Dt = 0.25f }.Run(serial);
        new MoveJob { Dt = 0.25f }.ScheduleParallel(parallel);
        That(SumPositions(parallel), Is.EqualTo(SumPositions(serial)).Within(0.001f));
    }

    [Test]
    public static void ScheduleParallel_IsDeterministic()
    {
        float expected;
        using (var world = CreateWorld(20_000))
        {
            new MoveJob { Dt = 1.5f }.Run(world);
            expected = SumPositions(world);
        }
        for (int run = 0; run < 5; run++)
        {
            using var world = CreateWorld(20_000);
            new MoveJob { Dt = 1.5f }.ScheduleParallel(world);
            That(SumPositions(world), Is.EqualTo(expected).Within(0.001f));
        }
    }

    [Test]
    public static void EntityParameter_IsAlignedWithComponents()
    {
        using var world = new World();
        var handles = new List<Entity>();
        for (int i = 0; i < 100; i++)
            handles.Add(world.Create(new JobPos { X = -1 }));

        new EntityStampJob().ScheduleParallel(world);

        foreach (var handle in handles)
            That(handle.Get<JobPos>().X, Is.EqualTo(EntityMarshal.EntityID(handle)));
    }

    [Test]
    public static void ByValueParameter_ReadsComponent()
    {
        using var world = new World();
        for (int i = 0; i < 50; i++)
            world.Create(new JobPos { X = i }, new JobVel());

        new CopyJob().Run(world);

        float sum = 0;
        foreach (var (p, v) in world.Query<JobPos, JobVel>().Enumerate<JobPos, JobVel>())
        {
            That(v.Value.X, Is.EqualTo(p.Value.X));
            sum += v.Value.X;
        }
        That(sum, Is.EqualTo(50 * 49 / 2).Within(0.001f));
    }

    [Test]
    public static void WithAll_SkipsEntitiesWithoutType()
    {
        using var world = new World();
        var plain = new List<Entity>();
        var tagged = new List<Entity>();
        for (int i = 0; i < 30; i++)
            plain.Add(world.Create(new JobPos()));
        for (int i = 0; i < 20; i++)
            tagged.Add(world.Create(new JobPos(), new JobTag()));

        new WithAllJob().Run(world);

        foreach (var handle in plain)
            That(handle.Get<JobPos>().X, Is.EqualTo(0));
        foreach (var handle in tagged)
            That(handle.Get<JobPos>().X, Is.EqualTo(1));
    }

    [Test]
    public static void WithNone_SkipsEntitiesWithType()
    {
        using var world = new World();
        var plain = new List<Entity>();
        var tagged = new List<Entity>();
        for (int i = 0; i < 30; i++)
            plain.Add(world.Create(new JobPos()));
        for (int i = 0; i < 20; i++)
            tagged.Add(world.Create(new JobPos(), new JobTag()));

        new WithNoneJob().Run(world);

        foreach (var handle in plain)
            That(handle.Get<JobPos>().X, Is.EqualTo(1));
        foreach (var handle in tagged)
            That(handle.Get<JobPos>().X, Is.EqualTo(0));
    }

    [Test]
    public static void MultipleExecuteMethods_Throws()
    {
        using var world = new World();
        Throws<InvalidOperationException>(() => new MultipleExecuteJob().Run(world));
    }

    [Test]
    public static void EntityNotFirst_Throws()
    {
        using var world = new World();
        Throws<InvalidOperationException>(() => new EntityNotFirstJob().Run(world));
    }
}

internal struct A8C1 { public int V; }
    internal struct A8C2 { public int V; }
    internal struct A8C3 { public int V; }
    internal struct A8C4 { public int V; }
    internal struct A8C5 { public int V; }
    internal struct A8C6 { public int V; }
    internal struct A8C7 { public int V; }
    internal struct A8C8 { public int V; }

    internal struct EightComponentJob : IJobEntity
    {
        private void Execute(ref A8C1 a, ref A8C2 b, ref A8C3 c, ref A8C4 d, in A8C5 e, in A8C6 f, A8C7 g, A8C8 h)
            => a.V = b.V + c.V + d.V + e.V + f.V + g.V + h.V;
    }

    internal struct EntityOnlyJob : IJobEntity
    {
        private int _unused;
        private void Execute(Entity entity) => _unused = EntityMarshal.EntityID(entity);
    }

    [WithAll(typeof(JobTag))]
    [WithNone(typeof(JobVel))]
    internal struct CombinedFilterJob : IJobEntity
    {
        private void Execute(ref JobPos p) => p.X = 2;
    }

    internal struct ThrowingJob : IJobEntity
    {
        public int Mod;
        private void Execute(ref JobPos p)
        {
            if (p.X == Mod)
                throw new ArgumentException("boom");
        }
    }

    [WithAny(typeof(JobTag), typeof(JobVel2))]
    internal struct WithAnyJob : IJobEntity
    {
        private void Execute(ref JobPos p) => p.X++;
    }

    internal struct JobVel2
    {
        public float X;
    }

    internal struct JobVel3
    {
        public float X;
    }

    [WithAny(typeof(JobVel2), typeof(JobVel3))]
    [WithNone(typeof(JobTag))]
    internal struct WithAnyNoneJob : IJobEntity
    {
        private void Execute(ref JobPos p) => p.X = 5;
    }

    internal static class JobEntityUseCaseTests
    {
        [Test]
        public static void ManyArchetypes_AllVisitedInParallel()
        {
            using var world = new World();
            var handles = new List<Entity>();
            for (int archetype = 0; archetype < 40; archetype++)
                for (int i = 0; i < 25; i++)
                    handles.Add(world.Create(new JobPos { X = 0 }, new JobVel { X = archetype + 1 }));

            new MoveJob { Dt = 1f }.ScheduleParallel(world);

            foreach (var handle in handles)
                That(handle.Get<JobPos>().X, Is.EqualTo(handle.Get<JobVel>().X));
        }

        [Test]
        public static void EightComponentJob_Works()
        {
            using var world = new World();
            var handle = world.Create(new A8C1(), new A8C2 { V = 2 }, new A8C3 { V = 3 }, new A8C4 { V = 4 },
                new A8C5 { V = 5 }, new A8C6 { V = 6 }, new A8C7 { V = 7 }, new A8C8 { V = 8 });
            new EightComponentJob().Run(world);
            That(handle.Get<A8C1>().V, Is.EqualTo(2 + 3 + 4 + 5 + 6 + 7 + 8));
        }

        [Test]
        public static void EntityOnlyJob_CoversAllEntities()
        {
            using var world = new World();
            var tagged = new List<Entity>();
            for (int i = 0; i < 10; i++)
                tagged.Add(world.Create(new JobPos()));
            for (int i = 0; i < 10; i++)
                world.Create(new JobTag());

            That(() => new EntityOnlyJob().Run(world), NUnit.Framework.Throws.Nothing);
        }

        [Test]
        public static void CombinedFilters_ApplyTogether()
        {
            using var world = new World();
            var plain = world.Create(new JobPos());
            var tagged = world.Create(new JobPos(), new JobTag());
            var full = world.Create(new JobPos(), new JobTag(), new JobVel());

            new CombinedFilterJob().Run(world);

            That(plain.Get<JobPos>().X, Is.EqualTo(0));
            That(tagged.Get<JobPos>().X, Is.EqualTo(2));
            That(full.Get<JobPos>().X, Is.EqualTo(0));
        }

        [Test]
        public static void ExceptionInExecute_PropagatesAndWorldStaysUsable()
        {
            using var world = JobEntityTests.CreateWorld(100);
            var aggregate = Throws<AggregateException>(() => new ThrowingJob { Mod = 0 }.ScheduleParallel(world));
            That(aggregate!.InnerException, Is.InstanceOf<ArgumentException>());

            var created = world.Create(new JobPos { X = 5 }, new JobVel { X = 1 });
            new MoveJob { Dt = 1f }.Run(world);
            That(created.Get<JobPos>().X, Is.EqualTo(6));
        }

        [Test]
        public static void TwoWorlds_SameJobPlan()
        {
            using var worldA = JobEntityTests.CreateWorld(50, 2f);
            using var worldB = JobEntityTests.CreateWorld(60, 3f);
            new MoveJob { Dt = 1f }.ScheduleParallel(worldA);
            new MoveJob { Dt = 1f }.ScheduleParallel(worldB);
            That(JobEntityTests.SumPositions(worldA), Is.EqualTo(50 * 2f).Within(0.001f));
            That(JobEntityTests.SumPositions(worldB), Is.EqualTo(60 * 3f).Within(0.001f));
        }

        [Test]
        public static void StructuralChangesBetweenSchedules()
        {
            using var world = JobEntityTests.CreateWorld(100);
            new MoveJob { Dt = 1f }.ScheduleParallel(world);
            for (int i = 0; i < 20; i++)
                world.Create(new JobPos { X = 0 }, new JobVel { X = 10 });
            new MoveJob { Dt = 1f }.ScheduleParallel(world);
            That(JobEntityTests.SumPositions(world), Is.EqualTo(100 * 1f + 100 * 1f + 20 * 10f).Within(0.001f));
        }

        [Test]
        public static void EmptyWorld_NoOp()
        {
            using var world = new World();
            That(() => new MoveJob { Dt = 1f }.ScheduleParallel(world), NUnit.Framework.Throws.Nothing);
        }

        [Test]
        public static void TinyWorld_Works()
        {
            using var world = JobEntityTests.CreateWorld(1);
            new MoveJob { Dt = 2f }.ScheduleParallel(world);
            That(JobEntityTests.SumPositions(world), Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public static void WithAny_RunsOncePerEntity()
        {
            using var world = new World();
            var plain = world.Create(new JobPos());
            var tagOnly = world.Create(new JobPos(), new JobTag());
            var vel2Only = world.Create(new JobPos(), new JobVel2 { X = 1 });
            var both = world.Create(new JobPos(), new JobTag(), new JobVel2 { X = 1 });

            new WithAnyJob().Run(world);

            That(plain.Get<JobPos>().X, Is.EqualTo(0));
            That(tagOnly.Get<JobPos>().X, Is.EqualTo(1));
            That(vel2Only.Get<JobPos>().X, Is.EqualTo(1));
            That(both.Get<JobPos>().X, Is.EqualTo(1));
        }

        [Test]
        public static void WithAny_MatchesAtLeastOneOfMany()
        {
            using var world = new World();
            var vel3 = world.Create(new JobPos(), new JobVel3 { X = 1 });
            var vel2And3 = world.Create(new JobPos(), new JobVel2 { X = 1 }, new JobVel3 { X = 1 });

            new WithAnyNoneJob().ScheduleParallel(world);

            That(vel3.Get<JobPos>().X, Is.EqualTo(5));
            That(vel2And3.Get<JobPos>().X, Is.EqualTo(5));
        }

        [Test]
        public static void WithAny_CombinesWithWithNone()
        {
            using var world = new World();
            var vel2 = world.Create(new JobPos(), new JobVel2 { X = 1 });
            var vel2Tagged = world.Create(new JobPos(), new JobVel2 { X = 1 }, new JobTag());
            var vel3 = world.Create(new JobPos(), new JobVel3 { X = 1 });

            new WithAnyNoneJob().Run(world);

            That(vel2.Get<JobPos>().X, Is.EqualTo(5));
            That(vel2Tagged.Get<JobPos>().X, Is.EqualTo(0));
            That(vel3.Get<JobPos>().X, Is.EqualTo(5));
        }

        [Test]
        public static void RepeatedScheduling_IsStable()
        {
            using var world = JobEntityTests.CreateWorld(1000);
            for (int i = 0; i < 100; i++)
                new MoveJob { Dt = 0.01f }.ScheduleParallel(world);
            That(JobEntityTests.SumPositions(world), Is.EqualTo(1000 * 1f).Within(0.1f));
        }
    }
