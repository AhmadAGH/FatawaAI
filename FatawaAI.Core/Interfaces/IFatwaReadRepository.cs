namespace FatawaAI.Core;

public interface IFatwaReadRepository
{
    Task<Fatwa?> GetByIdAsync(long fatwaId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Fatwa>> GetByIdsAsync(
        IReadOnlyCollection<long> fatwaIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetAllCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Vector search over single embedding, optionally filtered by category and/or collection.
    /// Returns up to <paramref name="limit"/> candidates ordered by similarity.
    /// </summary>
    Task<IReadOnlyList<FatwaCandidate>> SearchByEmbeddingAsync(
        float[] embedding,
        int limit,
        string? category,
        string? collectionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Vector search using per-field embeddings with weights.
    /// </summary>
    Task<IReadOnlyList<FatwaCandidate>> SearchByEmbeddingPerFieldAsync(
        float[] queryEmbedding,
        string queryText,
        SearchWeights weights,
        int limit,
        string? category,
        string? collectionType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hybrid search combining vector similarity and full-text search using Reciprocal Rank Fusion.
    /// Returns candidates ranked by combined relevance score.
    /// </summary>
    Task<IReadOnlyList<FatwaCandidate>> SearchHybridAsync(
        float[] queryEmbedding,
        string queryText,
        SearchWeights weights,
        int limit,
        string? category,
        string? collectionType,
        CancellationToken cancellationToken);
}


