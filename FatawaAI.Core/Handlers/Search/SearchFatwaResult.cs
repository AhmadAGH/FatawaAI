namespace FatawaAI.Core;

/// <summary>
/// Lightweight fatwa representation for search results.
/// </summary>
public sealed record SearchFatwaResult(
    long FatwaId,
    string CollectionType,
    int SourceId,
    string Title,
    string Question,
    string Answer,
    string SourceUrl,
    string? AudioUrl,
    IReadOnlyList<string> Categories
);

