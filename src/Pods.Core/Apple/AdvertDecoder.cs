namespace Pods.Core.Apple;

/// <summary>Decodes Apple proximity-pairing (type 0x07) manufacturer data, SPEC §5.1.</summary>
public static class AdvertDecoder
{
    public const int BatteryOffset = 5;
    const int FrameLength = 27;

    /// <param name="data">Manufacturer data after the 2-byte company ID.</param>
    public static AppleAdvert? Decode(ReadOnlySpan<byte> data, short rssi, ulong address)
    {
        if (data.Length < 11 || data[0] != 0x07 || data[1] + 2 != data.Length || data.Length != FrameLength || rssi <= -127)
            return null;

        var modelId = $"{data[3]:x2}{data[4]:x2}";
        var model = ModelCatalog.Find(modelId);
        var isMax = IsMaxFamily(model);

        var status = data[5];
        if (isMax)
        {
            // AirPods Max status mapping; unverified on hardware (SPEC §12).
            status = status switch { 0x24 => 0x22, 0x04 => 0x02, 0x20 => 0x22, 0x00 => 0x02, _ => status };
        }

        bool Bit(int i) => ((status >> i) & 1) == 1;
        var flag = Bit(6) == Bit(5);
        var rightInEar = flag ? Bit(1) : Bit(3);
        var leftInEar = flag ? Bit(3) : Bit(1);
        var primaryIsLeft = Bit(5);
        var bothPresent = Bit(0);
        if (isMax)
        {
            primaryIsLeft = true;
            bothPresent = false;
        }

        byte b6 = data[6], b7 = data[7];
        int high = b6 >> 4, low = b6 & 0x0F;
        var battery = new PodBattery(
            Left: Percent(primaryIsLeft ? low : high),
            Right: Percent(primaryIsLeft ? high : low),
            Case: Percent(b7 & 0x0F),
            LeftCharging: (b7 & (primaryIsLeft ? 0x10 : 0x20)) != 0,
            RightCharging: (b7 & (primaryIsLeft ? 0x20 : 0x10)) != 0,
            CaseCharging: (b7 & 0x40) != 0);

        // AirPods Max: colour code "11" is reported for the colour catalogued as "31". Unverified on hardware (SPEC §12).
        var colour = $"{data[9]:x2}";
        if (modelId == "0a20" && colour == "11") colour = "31";

        return new AppleAdvert(address, rssi, modelId, model, status, battery, bothPresent, primaryIsLeft,
            leftInEar, rightInEar, data[8], colour);
    }

    internal static bool IsMaxFamily(AppleModel? model) =>
        model is not null && ModelCatalog.MaxFamilyIds.Contains(model.Id);

    static int? Percent(int nibble) => nibble == 15 ? null : Math.Min(nibble * 10 + BatteryOffset, 100);
}
