using LetsChat.Domain.Entities;

namespace LetsChat.Application.Ports;

/// <summary>Outbound port: where registered devices live.</summary>
public interface IDeviceRepository
{
    /// <summary>Next free device number for the account (max + 1, starting at 1).</summary>
    Task<int> NextDeviceNumberAsync(Guid accountId, CancellationToken ct = default);

    Task AddAsync(Device device, CancellationToken ct = default);

    /// <summary>Find a device by its routing address ("username.N"), or null.</summary>
    Task<Device?> FindByAddressAsync(string address, CancellationToken ct = default);
}
