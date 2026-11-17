namespace Pods.Core.Devices;

/// <summary>A connect or disconnect the user asked for, and how it ended.</summary>
public enum ConnectionRequest { None, Connecting, Disconnecting, ConnectFailed, DisconnectFailed }
