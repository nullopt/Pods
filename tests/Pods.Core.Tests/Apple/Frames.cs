namespace Pods.Core.Tests.Apple;
internal static class Frames
{
    public static byte[] Make(byte m0, byte m1, byte status, byte b6, byte b7, byte cap = 0, byte colour = 0)
    {
        var b = new byte[27];
        b[0] = 0x07; b[1] = 0x19; b[2] = 0x01; b[3] = m0; b[4] = m1;
        b[5] = status; b[6] = b6; b[7] = b7; b[8] = cap; b[9] = colour;
        return b;
    }
    public static byte[] Pro2(byte status, byte b6 = 0x58, byte b7 = 0x13, byte cap = 0) => Make(0x14, 0x20, status, b6, b7, cap);
}
