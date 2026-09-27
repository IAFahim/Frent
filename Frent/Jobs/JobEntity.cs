using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Frent.Core;
using Frent.Core.Archetypes;
using Frent.Systems;
using Frent.Systems.Queries;

namespace Frent.Jobs;

/// <summary>
/// Runs <see cref="IJobEntity"/> jobs over their query, either inline or across the thread pool.
/// </summary>
public static partial class JobEntity
{
    /// <summary>
    /// Runs the job on the calling thread, over every entity matching its signature.
    /// </summary>
    /// <param name="job">The job to run.</param>
    /// <param name="world">The world to run against.</param>
    public static void Run<TJob>(this TJob job, World world) where TJob : struct, IJobEntity
    {
        var plan = JobPlan<TJob>.Instance;
        var query = plan.BuildQuery(world);
        world.EnterDisallowState();
        try
        {
            foreach (var archetype in query.AsSpan())
            {
                var count = archetype.EntityCount;
                if (count > 0)
                    plan.Runner(ref job, world, archetype, 0, count);
            }
        }
        finally
        {
            world.ExitDisallowState(null);
        }
    }

    /// <summary>
    /// Runs the job in parallel over entity ranges, splitting large archetypes so all workers stay busy.
    /// </summary>
    /// <param name="job">The job to run.</param>
    /// <param name="world">The world to run against.</param>
    public static void ScheduleParallel<TJob>(this TJob job, World world) where TJob : struct, IJobEntity
    {
        var plan = JobPlan<TJob>.Instance;
        var query = plan.BuildQuery(world);
        world.EnterDisallowState();
        try
        {
            var ranges = BuildRanges(query.AsSpan());
            if (ranges.Length == 0)
                return;
            var runner = plan.Runner;
            Parallel.For(0, ranges.Length, r =>
            {
                var localJob = job;
                var range = ranges[r];
                runner(ref localJob, world, range.Archetype, range.From, range.To);
            });
        }
        finally
        {
            world.ExitDisallowState(null);
        }
    }

    private readonly struct ArchetypeRange
    {
        public readonly Archetype Archetype;
        public readonly int From;
        public readonly int To;

        public ArchetypeRange(Archetype archetype, int from, int to)
        {
            Archetype = archetype;
            From = from;
            To = to;
        }
    }

    private static ArchetypeRange[] BuildRanges(Span<Archetype> archetypes)
    {
        long total = 0;
        for (var i = 0; i < archetypes.Length; i++)
            total += archetypes[i].EntityCount;
        if (total == 0)
            return Array.Empty<ArchetypeRange>();
        long boundedBatch = Math.Min(Math.Max(total / (Environment.ProcessorCount * 4), 512), int.MaxValue);
        int batch = (int)boundedBatch;
        var ranges = new ArchetypeRange[archetypes.Length + (int)(total / batch) + 1];
        var count2 = 0;
        for (var i = 0; i < archetypes.Length; i++)
        {
            var archetype = archetypes[i];
            var count = archetype.EntityCount;
            for (var from = 0; from < count; from += batch)
                ranges[count2++] = new ArchetypeRange(archetype, from, Math.Min(from + batch, count));
        }
        Array.Resize(ref ranges, count2);
        return ranges;
    }

    internal static Entity GetEntityAt(Span<EntityIDOnly> entities, int index, World world) => entities[index].ToEntity(world);

    internal static T GetComponentAt<T>(Span<T> components, int index) => components[index];
}

internal delegate void JobArchetypeRunner<TJob>(ref TJob job, World world, Archetype archetype, int from, int to) where TJob : struct, IJobEntity;

internal sealed class JobPlan<TJob> where TJob : struct, IJobEntity
{
    private static JobPlan<TJob>? s_Instance;
    public static JobPlan<TJob> Instance => s_Instance ??= Create();

    public readonly JobArchetypeRunner<TJob> Runner;
    public readonly Func<World, Query> BuildQuery;

    private JobPlan(JobArchetypeRunner<TJob> runner, Func<World, Query> buildQuery)
    {
        Runner = runner;
        BuildQuery = buildQuery;
    }

    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static JobPlan<TJob> Create()
    {
        var jobType = typeof(TJob);
        MethodInfo? execute = null;
        foreach (var method in jobType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (method.Name != "Execute")
                continue;
            if (execute is not null)
                throw new InvalidOperationException($"{jobType} declares multiple Execute methods; jobs support exactly one.");
            execute = method;
        }
        if (execute is null)
            throw new InvalidOperationException($"{jobType} does not declare an Execute method.");
        if (execute.ReturnType != typeof(void))
            throw new InvalidOperationException($"{jobType}.Execute must return void.");

        var parameters = execute.GetParameters();
        var hasEntity = parameters.Length > 0 && parameters[0].ParameterType == typeof(Entity);
        var first = hasEntity ? 1 : 0;
        var componentTypes = new List<Type>();
        var byRef = new List<bool>();
        for (var p = first; p < parameters.Length; p++)
        {
            var type = parameters[p].ParameterType;
            var isByRef = type.IsByRef;
            if (isByRef)
                type = type.GetElementType()!;
            if (type == typeof(Entity))
                throw new InvalidOperationException($"{jobType}.Execute: an Entity parameter is only supported as the first parameter.");
            if (!type.IsValueType || type.IsGenericType)
                throw new InvalidOperationException($"{jobType}.Execute: parameter {type} is not a component struct.");
            if (IsSparse(type))
                throw new InvalidOperationException($"{jobType}.Execute: sparse components are not supported by jobs.");
            if (!componentTypes.Contains(type))
                componentTypes.Add(type);
            byRef.Add(isByRef);
        }

        var queryTypes = new List<Type>(componentTypes);
        var noneTypes = new List<Type>();
        if (jobType.GetCustomAttribute<WithAllAttribute>() is { } withAll)
            foreach (var type in withAll.Types)
            {
                if (!type.IsValueType)
                    throw new InvalidOperationException($"{jobType}: [WithAll] requires component structs.");
                queryTypes.Add(type);
            }
        if (jobType.GetCustomAttribute<WithNoneAttribute>() is { } withNone)
            foreach (var type in withNone.Types)
            {
                if (!type.IsValueType)
                    throw new InvalidOperationException($"{jobType}: [WithNone] requires component structs.");
                noneTypes.Add(type);
            }

        var chain = typeof(QueryBuilder);
        foreach (var type in queryTypes)
            chain = typeof(QueryWith<,>).MakeGenericType(type, chain);
        foreach (var type in noneTypes)
            chain = typeof(QueryWithout<,>).MakeGenericType(type, chain);
        var closed = FindBuildQuery().MakeGenericMethod(chain);
        Func<World, Query> buildQuery = world => (Query)closed.Invoke(world, null)!;

        var runner = EmitRunner(execute, hasEntity, componentTypes, byRef);
        return new JobPlan<TJob>(runner, buildQuery);
    }

    private static MethodInfo FindBuildQuery()
    {
        foreach (var method in typeof(World).GetMethods(All))
            if (method.Name == "BuildQuery" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                return method;
        throw new InvalidOperationException("World.BuildQuery was not found.");
    }

    private static bool IsSparse(Type type)
    {
        var member = typeof(Component<>).MakeGenericType(type).GetMember("IsSparseComponent", All)[0];
        return member is FieldInfo field ? (bool)field.GetValue(null)! : (bool)((PropertyInfo)member).GetValue(null)!;
    }

    private static JobArchetypeRunner<TJob> EmitRunner(MethodInfo execute, bool hasEntity, List<Type> componentTypes, List<bool> byRef)
    {
        var runner = new DynamicMethod($"{typeof(TJob).Name}_JobRun", typeof(void),
            new[] { typeof(TJob).MakeByRefType(), typeof(World), typeof(Archetype), typeof(int), typeof(int) },
            typeof(JobEntity).Module, true);
        var il = runner.GetILGenerator();
        var getComponentSpan = typeof(Archetype).GetMethod("GetComponentSpan", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var getEntitySpan = typeof(Archetype).GetMethod("GetEntitySpan", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var spans = new LocalBuilder[componentTypes.Count];
        for (var c = 0; c < componentTypes.Count; c++)
        {
            spans[c] = il.DeclareLocal(typeof(Span<>).MakeGenericType(componentTypes[c]));
            il.Emit(OpCodes.Ldarg_2);
            il.EmitCall(OpCodes.Call, getComponentSpan.MakeGenericMethod(componentTypes[c]), null);
            il.Emit(OpCodes.Stloc, spans[c]);
        }
        LocalBuilder? entitySpan = null;
        if (hasEntity)
        {
            entitySpan = il.DeclareLocal(typeof(Span<EntityIDOnly>));
            il.Emit(OpCodes.Ldarg_2);
            il.EmitCall(OpCodes.Call, getEntitySpan, null);
            il.Emit(OpCodes.Stloc, entitySpan);
        }

        var i = il.DeclareLocal(typeof(int));
        var loop = il.DefineLabel();
        var done = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Stloc, i);
        il.MarkLabel(loop);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Bge, done);
        il.Emit(OpCodes.Ldarg_0);
        if (hasEntity)
        {
            il.Emit(OpCodes.Ldloc, entitySpan!);
            il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldarg_1);
            il.EmitCall(OpCodes.Call, typeof(JobEntity).GetMethod(nameof(JobEntity.GetEntityAt), BindingFlags.NonPublic | BindingFlags.Static)!, null);
        }
        for (var c = 0; c < componentTypes.Count; c++)
        {
            var type = componentTypes[c];
            if (byRef[c])
            {
                il.Emit(OpCodes.Ldloca_S, spans[c]);
                il.Emit(OpCodes.Ldloc, i);
                il.EmitCall(OpCodes.Call, typeof(Span<>).MakeGenericType(type).GetProperty("Item")!.GetGetMethod()!, null);
            }
            else
            {
                il.Emit(OpCodes.Ldloc, spans[c]);
                il.Emit(OpCodes.Ldloc, i);
                il.EmitCall(OpCodes.Call, typeof(JobEntity).GetMethod(nameof(JobEntity.GetComponentAt), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(type), null);
            }
        }
        il.EmitCall(OpCodes.Call, execute, null);
        il.Emit(OpCodes.Ldloc, i);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, i);
        il.Emit(OpCodes.Br_S, loop);
        il.MarkLabel(done);
        il.Emit(OpCodes.Ret);
        return (JobArchetypeRunner<TJob>)runner.CreateDelegate(typeof(JobArchetypeRunner<TJob>));
    }
}
