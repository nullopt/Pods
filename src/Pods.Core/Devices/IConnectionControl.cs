namespace Pods.Core.Devices;

/// <summary>Asks Windows to connect or disconnect a paired device. True if the request was sent.</summary>
public interface IConnectionControl
{
    Task<bool> ConnectAsync(string id);
    Task<bool> DisconnectAsync(string id);
}
