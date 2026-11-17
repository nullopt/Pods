namespace Pods.Core.Devices;

/// <summary>SPEC §4. Drives the device card's state chip.</summary>
public enum DeviceState { Disconnected, Searching, Connected, SearchingError, MultipleConnection }
