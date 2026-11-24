using WpfMqtt1.Models;

namespace WpfMqtt1.Services;

public interface IBrokerProfileStore
{
    event EventHandler? ProfilesChanged;

    Task<IReadOnlyList<BrokerProfile>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAllAsync(IEnumerable<BrokerProfile> profiles, CancellationToken cancellationToken = default);
    Task UpsertAsync(BrokerProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
