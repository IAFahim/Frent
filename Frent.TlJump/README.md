# Frent.TlJump

Smallest possible tl × Frent system: 4 entities run a 6-tick looping jump timeline plus a sound-event timeline, all on one shared clock column.

```text
frame 0: y = 4 4 4 4      frame 3: y = 7 7 7 7
frame 1: y = 7 7 7 7      frame 4: y = 4 4 4 4   ← land!
frame 2: y = 8 8 8 8      frame 5: y = 0 0 0 0   ← jump! (wrapped)
```

## The pattern

Frent chunk spans feed tl directly — zero copies, zero allocation:

```csharp
foreach (var (jumpTls, soundTls, clocks, jumps, sfxs) in
         world.Query<JumpTl, SoundTl, Clock, JumpY, Sfx>()
              .EnumerateChunks<JumpTl, SoundTl, Clock, JumpY, Sfx>())
{
    var jumpIds  = MemoryMarshal.Cast<JumpTl,  ushort>(jumpTls);
    var soundIds = MemoryMarshal.Cast<SoundTl, ushort>(soundTls);
    var clockCol = MemoryMarshal.Cast<Clock,   ushort>(clocks);
    var jumpCol  = MemoryMarshal.Cast<JumpY,   float>(jumps);
    var sfxCol   = MemoryMarshal.Cast<Sfx,     float>(sfxs);

    Timeline<JumpTrack,  JumpClip >.Apply(jumpIds,  clockCol, true, jumpCol);
    Timeline<SoundTrack, SoundClip>.Apply(soundIds, clockCol, true, sfxCol);
    Timeline.Step(jumpIds, clockCol, true);   // once per frame
}
```

- `Apply` is **read-only** on the clock — call it per pair, any order, no double-stepping.
- `Step` moves every clock exactly once per frame — this is the only mutating call.
- Components are single-field structs so `MemoryMarshal.Cast` turns `Span<Component>` into the `Span<ushort>`/`Span<float>` columns tl wants — both point at Frent's chunk memory.
- `Timeline.Step` needs one id column to pick the motion profile; the sound asset has the same duration/looping so the shared clock steps by the jump ids.

If a system owns its clock column outright (one pair only), the fused form does apply+step in one pass:

```csharp
Timeline<JumpTrack, JumpClip>.Apply(ids, clocks, clocks, true, jumpCol); // next == clocks: in-place
```

## Authoring

`DomainBaker` (test-support, linked into the sample) builds the asset bytes: tracks are `(Track, Clip)` pairs — `Clip(trackIndex, startTick, endTick, value)` — values at a tick are what the consumer's `Execute` sees via `frame.Clip`. `IBlend<TClip>.Blend` interpolates between adjacent clips inside a transition window — implement a real lerp or the first clip wins.

The consumer runs at measure/bind time, once per position, to build the delta tables that `Apply` gathers — runtime playback is pure table lookup, so `Execute` never sees per-frame state; put event codes in clip data and read them off the effect column (as `Sfx` does here).

`TimelineAsset.Load` → a `ushort` timeline index usable by every pair-typed `Timeline<T,C>` bank and `Timeline.Step`.
