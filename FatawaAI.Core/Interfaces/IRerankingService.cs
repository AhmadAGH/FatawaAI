namespace FatawaAI.Core;

public interface IRerankingService
{
    /// <summary>
    /// Given the original user query and vector-search candidates,
    /// returns the candidate fatwa IDs ordered by relevance (most relevant first).
    /// </summary>
    Task<IReadOnlyList<long>> RerankAsync(
        string userQuery,
        IReadOnlyList<FatwaCandidate> candidates,
        CancellationToken cancellationToken);
}

