namespace FatawaAI.Core;

public interface IFatwaWriteRepository
{
    /// <summary>
    /// Bulk insert or upsert fatwas (with embeddings), used by the import pipeline.
    /// Implementations should be efficient (e.g. COPY in PostgreSQL).
    /// </summary>
    Task BulkUpsertAsync(
        IEnumerable<FatwaWithEmbedding> fatwas,
        CancellationToken cancellationToken);
}

