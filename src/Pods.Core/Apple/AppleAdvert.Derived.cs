using PlaybackValue = Pods.Core.Apple.Playback;

namespace Pods.Core.Apple;

public enum Playback { AllIn, Partial, NoneIn }

public enum AudioOutHint { AnyIn, NoneIn }

/// <summary>Values derived from the decoded advert, SPEC §5.1 "Derived values".</summary>
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
        : AdvertDecoder.IsMaxFamily(Model) ? LidCounter >> 4 == 8   // Max rule unverified on hardware (SPEC §12)
        : Bit(2) && BothPresent && (LidCounter & 0x0F) <= 8;

    /// <summary>Null unless the model supports auto-connect.</summary>
    public bool? AutoConnect => !Has(AppleCapabilities.AutoConnect) ? null
        : Bit(6) && !Bit(2) && BothPresent && !LeftInEar && !RightInEar;

    /// <summary>Equality used for change detection and address matching; ignores address and RSSI.</summary>
    public bool SameState(AppleAdvert other) =>
        ModelId == other.ModelId && Colour == other.Colour && LidCounter == other.LidCounter && Battery == other.Battery;
}
