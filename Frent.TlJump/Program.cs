using System.Runtime.InteropServices;
using Frent;
using Tl;
using Tl.TestSupport;

unsafe class Program
{
    static void Main()
    {
        var jump = TimelineAsset.Load(new DomainBaker()
            .Track<JumpTrack, JumpClip>(new JumpTrack())
            .Clip(0, 0, 1, new JumpClip { Height = 4f })
            .Clip(0, 1, 2, new JumpClip { Height = 3f })
            .Clip(0, 2, 3, new JumpClip { Height = 1f })
            .Clip(0, 3, 4, new JumpClip { Height = -1f })
            .Clip(0, 4, 5, new JumpClip { Height = -3f })
            .Clip(0, 5, 6, new JumpClip { Height = -4f })
            .Looping()
            .Bake());
        var sound = TimelineAsset.Load(new DomainBaker()
            .Track<SoundTrack, SoundClip>(new SoundTrack())
            .Clip(0, 0, 1, new SoundClip { Code = 1 })
            .Clip(0, 5, 6, new SoundClip { Code = 2 })
            .Looping()
            .Bake());

        PairRuntime<JumpTrack, JumpClip>.Consume(&JumpEffect, &BindFloat);
        PairRuntime<SoundTrack, SoundClip>.Consume(&SoundEffect, &BindFloat);

        using var world = new World();
        for (var i = 0; i < 4; i++)
            world.Create<JumpTl, SoundTl, Clock, JumpY, Sfx>(
                new JumpTl(jump), new SoundTl(sound), new Clock(0), new JumpY(), new Sfx());

        for (var frame = 0; frame < 14; frame++)
        {
            foreach (var (jumpTls, soundTls, clocks, jumps, sfxs) in
                     world.Query<JumpTl, SoundTl, Clock, JumpY, Sfx>()
                          .EnumerateChunks<JumpTl, SoundTl, Clock, JumpY, Sfx>())
            {
                var jumpIds = MemoryMarshal.Cast<JumpTl, ushort>(jumpTls);
                var soundIds = MemoryMarshal.Cast<SoundTl, ushort>(soundTls);
                var clockCol = MemoryMarshal.Cast<Clock, ushort>(clocks);
                var jumpCol = MemoryMarshal.Cast<JumpY, float>(jumps);
                var sfxCol = MemoryMarshal.Cast<Sfx, float>(sfxs);

                Timeline<JumpTrack, JumpClip>.Apply(jumpIds, clockCol, true, jumpCol);
                Timeline<SoundTrack, SoundClip>.Apply(soundIds, clockCol, true, sfxCol);
                Timeline.Step(jumpIds, clockCol, true);

                for (var k = 0; k < sfxCol.Length; k++)
                {
                    if (sfxCol[k] == 1f) Console.WriteLine($"  entity {k}: jump!");
                    if (sfxCol[k] == 2f) Console.WriteLine($"  entity {k}: land!");
                    sfxCol[k] = 0f;
                }
                Console.Write($"frame {frame}: y =");
                for (var k = 0; k < jumpCol.Length; k++) Console.Write($" {jumpCol[k]:F0}");
                Console.WriteLine();
            }
        }
    }

    static void BindFloat(ulong* keys, int n, byte* table)
    {
        for (var i = 0; i < n; i++)
            if (keys[i] == TypeKey<float>.Value) { table[0] = (byte)(i + 1); return; }
    }

    static void JumpEffect(byte* slot, byte* pair, ushort tick, FrameFlags flags, void** columns, int row)
    {
        var s = default(JumpClip);
        ((float*)columns[0])[row] += TickFrame.ToFrame<JumpTrack, JumpClip>(slot, pair, tick, flags, ref s).Clip.Height;
    }

    static void SoundEffect(byte* slot, byte* pair, ushort tick, FrameFlags flags, void** columns, int row)
    {
        var s = default(SoundClip);
        ((float*)columns[0])[row] += TickFrame.ToFrame<SoundTrack, SoundClip>(slot, pair, tick, flags, ref s).Clip.Code;
    }
}

public struct JumpTl { public ushort Id; public JumpTl(ushort v) => Id = v; }
public struct SoundTl { public ushort Id; public SoundTl(ushort v) => Id = v; }
public struct Clock { public ushort Value; public Clock(ushort v) => Value = v; }
public struct JumpY { public float Value; }
public struct Sfx { public float Value; }

public struct JumpTrack : IBlend<JumpClip>
{
    public void Blend(in JumpClip a, in JumpClip b, float t, out JumpClip o)
        => o = new JumpClip { Height = a.Height + (b.Height - a.Height) * t };
}
public struct JumpClip { public float Height; }
public struct SoundTrack : IBlend<SoundClip> { public void Blend(in SoundClip a, in SoundClip b, float t, out SoundClip o) => o = a; }
public struct SoundClip { public float Code; }
