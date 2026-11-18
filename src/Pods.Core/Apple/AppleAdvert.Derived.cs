using PlaybackValue = Pods.Core.Apple.Playback;

namespace Pods.Core.Apple;

public enum Playback { AllIn, Partial, NoneIn }

public enum AudioOutHint { AnyIn, NoneIn }

/// <summary>Values derived from the decoded advert.</summary>
public sealed partial record AppleAdvert
{
    AppleCapabilities Capabilities => Model?.Capabilities ?? AppleCapabilities.None;
    bool Has(AppleCapabilities c) => (Capabilities & c) == c;
    bool Bit(int i) => ((Status >> i) & 1) == 1;
    bool PresentPodInEar => PrimaryIsLeft ? LeftInEar : RightInEar;

    /// <summary>Null unless the model supports ear detection.</summary>
    public Playback? Playback => !Has(AppleCapabilities.EarDetection) ? null
        : BothPresent
            ? (LeftInEar && RightInEar ? PlaybackValue.AllIn : !LeftInEar && !RightInEar ? PlaybackValue.NoneIn : PlaybackValue.Partial)
            : (PresentPodInEar ? PlaybackValue.AllIn : PlaybackValue.Partial);

    /// <summary>Null unless the model supports ear detection.</summary>
    public AudioOutHint? SwitchAudioOut => !Has(AppleCapabilities.EarDetection) ? null
        : (BothPresent ? LeftInEar || RightInEar : PresentPodInEar) ? AudioOutHint.AnyIn : AudioOutHint.NoneIn;

    /// <summary>Null unless the model supports the lid popup.</summary>
    public bool? LidPopup => !Has(AppleCapabilities.LidPopup) ? null
        : AdvertDecoder.IsMaxFamily(Model) ? LidCounter >> 4 == 8   // Max: measured transient on any state change, incl. leaving the case
        : Bit(2) && BothPresent && (LidCounter & 0x0F) <= 8;

    /// <summary>Both pods are in the case (status bit 2; bit 4 alone means one pod). Never for headphones without a case.</summary>
    public bool BothInCase => !ModelCatalog.SingleBatteryIds.Contains(ModelId) && BothPresent && Bit(2);

    /// <summary>Equality used for change detection and address matching; ignores address and RSSI.</summary>
    public bool SameState(AppleAdvert other) =>
        ModelId == other.ModelId && Colour == other.Colour && LidCounter == other.LidCounter && Battery == other.Battery;
}
