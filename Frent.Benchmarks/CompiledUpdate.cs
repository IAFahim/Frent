using BenchmarkDotNet.Attributes;
using Frent.Components;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace Frent.Benchmarks;

internal delegate void UpdateRangeDelegate<TComp, TArg>(Span<TComp> comps, Span<TArg> args, int from, int to) where TComp : struct;

// Prototype: compiles a per-component update loop with Reflection.Emit, replacing the runner's
// per-entity interface dispatch with a direct call that the JIT can inline. Same components,
// same attributes, same world.Update semantics - only the dispatch differs.
internal static class CompiledUpdate<TComp, TArg>
    where TComp : struct, IUpdate<TArg>
{
    private static readonly UpdateRangeDelegate<TComp, TArg> RunRange = Build();

    public static void Run(World world)
    {
        foreach (var chunk in world.Query<TComp, TArg>().EnumerateChunks<TComp, TArg>())
        {
            chunk.Deconstruct(out var comps, out var args);
            RunRange(comps, args, 0, comps.Length);
        }
    }

    public static void RunParallel(World world)
    {
        long total = 0;
        foreach (var chunk in world.Query<TComp, TArg>().EnumerateChunks<TComp, TArg>())
        {
            chunk.Deconstruct(out var comps, out _);
            total += comps.Length;
        }
        if (total == 0)
            return;
        int workers = Environment.ProcessorCount;
        long step = (total + workers - 1) / workers;
        Parallel.For(0, workers, w =>
        {
            long from = (long)w * step;
            long to = Math.Min(from + step, total);
            long offset = 0;
            foreach (var chunk in world.Query<TComp, TArg>().EnumerateChunks<TComp, TArg>())
            {
                chunk.Deconstruct(out var comps, out var args);
                long lo = Math.Max(from, offset);
                long hi = Math.Min(to, offset + comps.Length);
                if (lo < hi)
                    RunRange(comps, args, (int)(lo - offset), (int)(hi - offset));
                offset += comps.Length;
            }
        });
    }

    private static UpdateRangeDelegate<TComp, TArg> Build()
    {
        var map = typeof(TComp).GetInterfaceMap(typeof(IUpdate<TArg>));
        MethodInfo update = null!;
        for (int m = 0; m < map.InterfaceMethods.Length; m++)
            if (map.InterfaceMethods[m].Name == "Update")
                update = map.TargetMethods[m];
        var getComp = typeof(Span<>).MakeGenericType(typeof(TComp)).GetProperty("Item")!.GetGetMethod()!;
        var getArg = typeof(Span<>).MakeGenericType(typeof(TArg)).GetProperty("Item")!.GetGetMethod()!;
        var dm = new DynamicMethod($"{typeof(TComp).Name}_CompiledUpdate", typeof(void),
            new[] { typeof(Span<TComp>), typeof(Span<TArg>), typeof(int), typeof(int) }, typeof(CompiledUpdate<,>).Module, true);
        var il = dm.GetILGenerator();
        var i = il.DeclareLocal(typeof(int));
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Stloc, i);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldarga_S, (byte)0);
        il.Emit(OpCodes.Ldloc, i);
        il.EmitCall(OpCodes.Call, getComp, null);
        il.Emit(OpCodes.Ldarga_S, (byte)1);
        il.Emit(OpCodes.Ldloc, i);
        il.EmitCall(OpCodes.Call, getArg, null);
        il.EmitCall(OpCodes.Call, update, null);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);
        il.Emit(OpCodes.Br_S, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
        return (UpdateRangeDelegate<TComp, TArg>)dm.CreateDelegate(typeof(UpdateRangeDelegate<TComp, TArg>));
    }
}

internal struct UpdateBenchComponent : IUpdate<float>
{
    public float X;
    public float V;
    [Multithread]
    [Singlethread]
    public void Update(ref float dt) => X += V * dt;
}

public class CompiledUpdateBenchmark
{
    private World World = null!;

    [GlobalSetup]
    public void Setup()
    {
        World = new World();
        for (int i = 0; i < 1_000_000; i++)
            World.Create(new UpdateBenchComponent { X = i, V = 1 }, 0.5f);
        World.Update<Singlethread>();
        World.Update<Multithread>();
        CompiledUpdate<UpdateBenchComponent, float>.Run(World);
        CompiledUpdate<UpdateBenchComponent, float>.RunParallel(World);
    }

    [Benchmark(Baseline = true)]
    public void WorldUpdateSingle() => World.Update<Singlethread>();

    [Benchmark]
    public void WorldUpdateMulti() => World.Update<Multithread>();

    [Benchmark]
    public void CompiledSingle() => CompiledUpdate<UpdateBenchComponent, float>.Run(World);

    [Benchmark]
    public void CompiledParallel() => CompiledUpdate<UpdateBenchComponent, float>.RunParallel(World);
}
