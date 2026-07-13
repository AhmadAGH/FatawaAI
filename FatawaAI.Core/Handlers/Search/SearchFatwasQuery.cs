using MediatR;

namespace FatawaAI.Core;

/// <summary>
/// Main search query used by the API layer.
/// </summary>
public sealed record SearchFatwasQuery(
    string Query,
    string? Category,
    string? CollectionType
) : IRequest<SearchFatwasResponse>;

