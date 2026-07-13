namespace FatawaAI.Core;

/// <summary>
/// Service for filtering candidates by semantic relevance using LLM classification.
/// </summary>
public interface ISemanticFilterService
{
    /// <summary>
    /// Filters candidates by classifying them as RELEVANT, PARTIAL, or IRRELEVANT
    /// based on semantic similarity to the user query.
    /// Returns only RELEVANT and high-scoring PARTIAL candidates.
    /// </summary>
    Task<IReadOnlyList<FatwaCandidate>> FilterAsync(
        string userQuery,
        IReadOnlyList<FatwaCandidate> candidates,
        CancellationToken cancellationToken);
}

