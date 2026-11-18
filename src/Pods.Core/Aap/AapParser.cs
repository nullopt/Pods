using Pods.Core.Apple;

namespace Pods.Core.Aap;

public abstract record AapMessage;

/// <summary>Exact battery: null where a part did not report or reported itself disconnected.</summary>
public sealed record AapBatteryMessage(PodBattery Battery) : AapMessage;

public sealed record AapNoiseControlMessage(NoiseControlMode Mode) : AapMessage;

/// <summary>Where each pod is; the order is the headset's own (primary first), not left/right.</summary>
public sealed record AapEarMessage(AapEarState Primary, AapEarState Secondary) : AapMessage
{
    /// <summary>
    /// Same meaning as the advert's playback: all present pods in ear, some, or none. A single present
    /// pod (or the AirPods Max) out of ear counts as partial, as in the advert. Null when neither is present.
    /// </summary>
    public Playback? Playback => (Present(Primary), Present(Secondary)) switch
    {
        (true, true) => (Primary, Secondary) switch
        {
            (AapEarState.InEar, AapEarState.InEar) => Apple.Playback.AllIn,
            (AapEarState.InEar, _) or (_, AapEarState.InEar) => Apple.Playback.Partial,
            _ => Apple.Playback.NoneIn,
        },
        (true, false) => Primary == AapEarState.InEar ? Apple.Playback.AllIn : Apple.Playback.Partial,
        (false, true) => Secondary == AapEarState.InEar ? Apple.Playback.AllIn : Apple.Playback.Partial,
        _ => null,
    };

    static bool Present(AapEarState state) => state is AapEarState.InEar or AapEarState.OutOfEar or AapEarState.InCase;
}

public enum AapEarState : byte { InEar = 0, OutOfEar = 1, InCase = 2, Absent = 3 }

/// <summary>Incoming AAP frames (analysis 02 §2.5). Anything else, or anything short, is null.</summary>
public static class AapParser
{
    const byte OpBattery = 0x04, OpEar = 0x06, OpSetting = 0x09;
    const byte PartSingle = 0x01, PartRight = 0x02, PartLeft = 0x04, PartCase = 0x08;
    const byte StatusCharging = 0x01, StatusDisconnected = 0x04;

    public static AapMessage? Parse(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 6 || frame[0] != 0x04 || frame[1] != 0x00 || frame[2] != 0x04 || frame[3] != 0x00 || frame[5] != 0x00) return null;
        return frame[4] switch
        {
            OpBattery => Battery(frame),
            OpEar when frame.Length >= 8 => Ear(frame[6], frame[7]),
            OpSetting when frame.Length >= 8 && frame[6] == AapFrames.SettingNoiseControl
                && frame[7] is >= (byte)NoiseControlMode.Off and <= (byte)NoiseControlMode.Adaptive
                => new AapNoiseControlMessage((NoiseControlMode)frame[7]),
            _ => null,
        };
    }

    /// <summary><c>count</c> at 6, then 5-byte entries <c>part ? level status ?</c> from 7.</summary>
    static AapBatteryMessage? Battery(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 7) return null;
        int count = frame[6];
        if (count == 0 || frame.Length < 7 + 5 * count) return null;
        var battery = new PodBattery(null, null, null, false, false, false);
        for (var i = 0; i < count; i++)
        {
            var entry = frame.Slice(7 + 5 * i, 5);
            var disconnected = entry[3] == StatusDisconnected;
            int? level = disconnected ? null : Math.Clamp((int)entry[2], 0, 100);
            var charging = !disconnected && entry[3] == StatusCharging;
            battery = entry[0] switch
            {
                PartSingle or PartLeft => battery with { Left = level, LeftCharging = charging },
                PartRight => battery with { Right = level, RightCharging = charging },
                PartCase => battery with { Case = level, CaseCharging = charging },
                _ => battery,
            };
        }
        return new AapBatteryMessage(battery);
    }

    static AapEarMessage? Ear(byte primary, byte secondary) =>
        primary > 3 || secondary > 3 ? null : new AapEarMessage((AapEarState)primary, (AapEarState)secondary);
}
