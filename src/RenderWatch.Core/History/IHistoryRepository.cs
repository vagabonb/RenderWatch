using RenderWatch.Core.Models;

namespace RenderWatch.Core.History;

public interface IHistoryRepository
{
    Task<IReadOnlyList<RenderSession>> GetAllSessionsAsync();
    Task AddSessionAsync(RenderSession session);
    Task UpdateSessionAsync(RenderSession session);
    Task DeleteSessionAsync(string id);
    Task ClearAllAsync();
    Task ExportToCsvAsync(string filePath, IEnumerable<RenderSession>? sessions = null);
}