using System;
using System.Collections.Generic;
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
    private static World CreateWorld(int count, float vel = 1f)
    {
        var world = new World();
        for (int i = 0; i < count; i++)
            world.Create(new JobPos { X = 0 }, new JobVel { X = vel });
        return world;
    }

    private static float SumPositions(World world)
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
