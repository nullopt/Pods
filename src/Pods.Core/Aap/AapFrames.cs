namespace Pods.Core.Aap;

/// <summary>Noise control modes, numbered as AAP setting 0x0D carries them.</summary>
public enum NoiseControlMode : byte { Off = 1, NoiseCancellation = 2, Transparency = 3, Adaptive = 4 }

/// <summary>
/// Outgoing AAP frames. Every frame after the handshake starts <c>04 00 04 00</c> and a 16-bit
/// little-endian opcode. Bytes as observed in MagicPods (analysis 02 §2.2, §2.3).
/// </summary>
public static class AapFrames
{
    /// <summary>Opens the session; sent first.</summary>
    public static ReadOnlyMemory<byte> Handshake { get; } =
        new byte[] { 0x00, 0x00, 0x04, 0x00, 0x01, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    /// <summary>Asks for every notification (battery, ear state, settings).</summary>
    public static ReadOnlyMemory<byte> EnableNotifications { get; } =
        new byte[] { 0x04, 0x00, 0x04, 0x00, 0x0F, 0x00, 0xFF, 0xFF, 0xFF, 0xFF };

    /// <summary>Extended mode, sent by MagicPods to the Adaptive-capable (H2) models only.</summary>
    public static ReadOnlyMemory<byte> ExtendedInit { get; } =
        new byte[] { 0x04, 0x00, 0x04, 0x00, 0x4D, 0x00, 0x0E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

    public const byte SettingNoiseControl = 0x0D;

    /// <summary>Control command: <c>04 00 04 00 09 00 &lt;setting&gt; &lt;v1..v4&gt;</c>.</summary>
    public static byte[] Setting(byte setting, byte value) =>
        [0x04, 0x00, 0x04, 0x00, 0x09, 0x00, setting, value, 0x00, 0x00, 0x00];

    public static byte[] SetNoiseControl(NoiseControlMode mode) => Setting(SettingNoiseControl, (byte)mode);
}
