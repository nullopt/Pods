namespace Pods.Core.Devices;

/// <summary>A Bluetooth device paired with this PC. <see cref="Id"/> is its address as 12 lowercase hex digits.</summary>
public sealed record PairedDevice(string Id, string Name, string ModelId, bool IsConnected);
