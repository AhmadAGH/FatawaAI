namespace FatawaAI.Core;

/// <summary>
/// DTO returned to the API from the search handler.
/// </summary>
public sealed record SearchFatwasResponse(
    string Query,
    QueryAnalysisResult? Analysis,
    IReadOnlyList<SearchFatwaResult> Results,
    string SourceAttribution,
    string Disclaimer
);

