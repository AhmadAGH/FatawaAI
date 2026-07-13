namespace FatawaAI.Core;

/// <summary>
/// DTO for POST /api/search.
/// Lives in Core so it can be reused by other frontends as well.
/// </summary>
public sealed record SearchDto(
    string Query,
    string? Category,
    string? CollectionType
);

