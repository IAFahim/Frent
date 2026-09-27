using System;

namespace Frent.Jobs;

/// <summary>
/// Marker for a job struct that runs an <c>Execute</c> method over every entity matching its component signature.
/// </summary>
/// <remarks>
/// Declare one instance <c>Execute</c> method. Each parameter selects a component: pass components by <c>ref</c> to write them,
/// by value or <c>in</c> to read them. A single <see cref="Frent.Entity"/> parameter, if present, must come first and
/// supplies the current entity. Other data travels in job fields; like Unity, the job is passed by value, so field writes inside <c>Execute</c> are not visible to the caller. The first call to <see cref="JobEntity.Run{TJob}"/> or
/// <see cref="JobEntity.ScheduleParallel{TJob}"/> compiles a tight loop for the job type and reuses it.
/// </remarks>
public interface IJobEntity
{
}

/// <summary>
/// Additional component types the job's query must match, beyond those in the <c>Execute</c> signature.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class WithAllAttribute(params Type[] types) : Attribute
{
    /// <summary>Component types required by the query.</summary>
    public Type[] Types = types;
}

/// <summary>
/// Component types the job's query must match at least one of.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class WithAnyAttribute(params Type[] types) : Attribute
{
    /// <summary>Component types of which at least one must be present.</summary>
    public Type[] Types = types;
}

/// <summary>
/// Component types the job's query must not match.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class WithNoneAttribute(params Type[] types) : Attribute
{
    /// <summary>Component types excluded from the query.</summary>
    public Type[] Types = types;
}
