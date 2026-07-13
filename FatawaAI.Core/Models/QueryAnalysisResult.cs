namespace FatawaAI.Core;

/// <summary>
/// Result of analyzing a user's natural-language query.
/// </summary>
public sealed record QueryAnalysisResult(
    string? FiqhTopic,
    IReadOnlyList<string> Keywords,
    string? Summary
);


