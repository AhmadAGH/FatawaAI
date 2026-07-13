namespace FatawaAI.Core;

/// <summary>
/// Import DTO including the embedding vector to persist into PostgreSQL.
/// Only title and question are embedded; answer is stored but not embedded.
/// </summary>
public sealed record FatwaWithEmbedding(
    string CollectionType,
    int SourceId,
    string Title,
    string Question,
    string Answer,
    string SourceUrl,
    string? AudioUrl,
    IReadOnlyList<string> Categories,
    float[] EmbeddingTitle,
    float[] EmbeddingQuestion
);


