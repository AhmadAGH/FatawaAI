using FatawaAI.Core;
using MediatR;

namespace FatawaAI.Domain;

/// <summary>
/// Implements the multi-stage RAG-based search pipeline:
/// Stage 1) Query Understanding & Validation with LLM
/// Stage 2) Multi-Strategy Retrieval (Hybrid: Vector + FTS)
/// Stage 3) LLM Semantic Filtering (eliminate irrelevant topics)
/// Stage 4) LLM Fine Re-ranking (order by specificity)
/// Stage 5) Final Validation & Threshold Check
/// </summary>
public sealed class SearchFatwasQueryHandler
    : IRequestHandler<SearchFatwasQuery, SearchFatwasResponse>
{
    private const int HybridCandidateCount = 120; // Increased for multi-strategy retrieval
    private const int SemanticFilterBatchSize = 15; // Send only top 15 to semantic filter (llama3.1:8b 4K limit)
    private const int MaxResults = 5;
    private const double MaxVectorDistanceThreshold = 25; // If closest result is farther than this, return empty
    private const int MinFilteredCandidates = 3; // If semantic filter returns fewer than this, confidence is low
    private const double FilterRetentionThreshold = 0.15; // If we filter out >85% of candidates, confidence is low

    // Required business strings
    private const string SourceAttribution =
        "من موقع سماحة الشيخ ابن باز - binbaz.org.sa";

    private const string NotFoundDisclaimer =
        "لم أجد فتوى مطابقة في مصادر ابن باز، يرجى مراجعة عالم مباشرة.";

    private const string GeneralDisclaimer =
        "هذا النظام يعرض فتاوى من موقع سماحة الشيخ ابن باز فقط، ولا يصدر أحكامًا شرعية جديدة. ينبغي دائمًا مراجعة أهل العلم في المسائل الحساسة.";

    private readonly IFatwaReadRepository _fatwaReadRepository;
    private readonly IEmbeddingService _embeddingService;
    private readonly IQueryAnalysisService _queryAnalysisService;
    private readonly ISemanticFilterService _semanticFilterService;
    private readonly IRerankingService _rerankingService;

    public SearchFatwasQueryHandler(
        IFatwaReadRepository fatwaReadRepository,
        IEmbeddingService embeddingService,
        IQueryAnalysisService queryAnalysisService,
        ISemanticFilterService semanticFilterService,
        IRerankingService rerankingService)
    {
        _fatwaReadRepository = fatwaReadRepository;
        _embeddingService = embeddingService;
        _queryAnalysisService = queryAnalysisService;
        _semanticFilterService = semanticFilterService;
        _rerankingService = rerankingService;
    }

    public async Task<SearchFatwasResponse> Handle(
        SearchFatwasQuery request,
        CancellationToken cancellationToken)
    {
        var trimmedQuery = request.Query?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedQuery))
        {
            // Empty query: return safe response with no results.
            return new SearchFatwasResponse(
                Query: trimmedQuery,
                Analysis: null,
                Results: Array.Empty<SearchFatwaResult>(),
                SourceAttribution: SourceAttribution,
                Disclaimer: NotFoundDisclaimer);
        }

        if (trimmedQuery.Length > 500)
        {
            trimmedQuery = trimmedQuery[..500];
        }

        // ========== STAGE 1: Query Understanding & Validation ==========
        QueryAnalysisResult? analysis = null;
        try
        {
            analysis = await _queryAnalysisService.AnalyzeAsync(
                trimmedQuery,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Swallow analysis errors: we can still do search on raw query.
            Console.WriteLine($"Query analysis error: {ex.Message}");
        }

        // Build text for embedding: prefer enriched summary from LLM analysis (more standardized)
        var textForEmbedding = !string.IsNullOrWhiteSpace(analysis?.Summary)
            ? analysis.Summary
            : BuildEmbeddingText(trimmedQuery, analysis);

        // Embed query.
        var embedding = await _embeddingService.EmbedAsync(
            textForEmbedding,
            cancellationToken);

        // ========== STAGE 2: Multi-Strategy Retrieval ==========
        var weights = SearchWeights.Default; // Title 0.6, Question 0.4
        
        // Use hybrid search (combines vector + full-text search with RRF)
        var candidates = await _fatwaReadRepository.SearchHybridAsync(
            embedding,
            trimmedQuery,
            weights,
            HybridCandidateCount,
            request.Category,
            request.CollectionType,
            cancellationToken);

        if (candidates.Count == 0)
        {
            return new SearchFatwasResponse(
                Query: trimmedQuery,
                Analysis: analysis,
                Results: Array.Empty<SearchFatwaResult>(),
                SourceAttribution: SourceAttribution,
                Disclaimer: NotFoundDisclaimer);
        }

        // ========== STAGE 3: LLM Semantic Filtering ==========
        // Filter out completely irrelevant topics before expensive re-ranking
        // Only send top N candidates to avoid LLM timeouts/poor quality
        var topCandidates = candidates.Take(SemanticFilterBatchSize).ToList();
        var filtered = await _semanticFilterService.FilterAsync(
            trimmedQuery,
            topCandidates,
            cancellationToken);

        // Confidence check: if too few candidates remain after filtering, quality is questionable
        var filterRetentionRate = (double)filtered.Count / candidates.Count;
        var hasLowConfidence = filtered.Count < MinFilteredCandidates || 
                               filterRetentionRate < FilterRetentionThreshold;

        if (filtered.Count == 0)
        {
            return new SearchFatwasResponse(
                Query: trimmedQuery,
                Analysis: analysis,
                Results: Array.Empty<SearchFatwaResult>(),
                SourceAttribution: SourceAttribution,
                Disclaimer: NotFoundDisclaimer);
        }

        // Log confidence metrics for monitoring
        Console.WriteLine($"Semantic filter: {candidates.Count} -> {filtered.Count} candidates " +
                         $"(retention: {filterRetentionRate:P1}, confidence: {(hasLowConfidence ? "LOW" : "HIGH")})");

        // ========== STAGE 4: LLM Fine Re-ranking ==========
        // Re-rank remaining candidates by specificity and exact match
        var rerankedIds = await _rerankingService.RerankAsync(
            trimmedQuery,
            filtered,
            cancellationToken);
        var ordered = OrderCandidates(filtered, rerankedIds);

        // ========== STAGE 5: Final Validation & Threshold ==========
        // Apply vector distance threshold as final quality check
        var qualityCandidates = ordered
            .Where(c => c.VectorScore <= MaxVectorDistanceThreshold || c.VectorScore == 0.0)
            .ToList();

        if (qualityCandidates.Count == 0)
        {
            return new SearchFatwasResponse(
                Query: trimmedQuery,
                Analysis: analysis,
                Results: Array.Empty<SearchFatwaResult>(),
                SourceAttribution: SourceAttribution,
                Disclaimer: NotFoundDisclaimer);
        }

        // Additional confidence check: if we had low confidence from filtering and still have
        // very few quality candidates, return empty with disclaimer to be safe
        if (hasLowConfidence && qualityCandidates.Count < 2)
        {
            Console.WriteLine($"Low confidence search: only {qualityCandidates.Count} quality candidate(s) after filtering. Returning empty.");
            return new SearchFatwasResponse(
                Query: trimmedQuery,
                Analysis: analysis,
                Results: Array.Empty<SearchFatwaResult>(),
                SourceAttribution: SourceAttribution,
                Disclaimer: NotFoundDisclaimer);
        }

        var top = qualityCandidates.Take(MaxResults).ToList();

        // 6) Load full fatwa records for the final results.
        var finalIds = top.Select(c => c.FatwaId).Distinct().ToArray();
        var fullFatwas = await _fatwaReadRepository.GetByIdsAsync(
            finalIds,
            cancellationToken);
        var fullById = fullFatwas.ToDictionary(f => f.FatwaId);

        var results = new List<SearchFatwaResult>(top.Count);
        foreach (var candidate in top)
        {
            if (!fullById.TryGetValue(candidate.FatwaId, out var fatwa))
            {
                continue;
            }

            results.Add(new SearchFatwaResult(
                FatwaId: fatwa.FatwaId,
                CollectionType: fatwa.CollectionType,
                SourceId: fatwa.SourceId,
                Title: fatwa.Title,
                Question: fatwa.Question,
                Answer: fatwa.Answer,
                SourceUrl: fatwa.SourceUrl,
                AudioUrl: fatwa.AudioUrl,
                Categories: fatwa.Categories));
        }

        var disclaimer = results.Count == 0 ? NotFoundDisclaimer : GeneralDisclaimer;

        return new SearchFatwasResponse(
            Query: trimmedQuery,
            Analysis: analysis,
            Results: results,
            SourceAttribution: SourceAttribution,
            Disclaimer: disclaimer);
    }

    private static string BuildEmbeddingText(string query, QueryAnalysisResult? analysis)
    {
        if (analysis is null)
        {
            return query;
        }

        var parts = new List<string> { query };

        if (!string.IsNullOrWhiteSpace(analysis.FiqhTopic))
        {
            parts.Add($"الموضوع الفقهي: {analysis.FiqhTopic}");
        }

        if (analysis.Keywords.Count > 0)
        {
            parts.Add("كلمات مفتاحية: " + string.Join(", ", analysis.Keywords));
        }

        if (!string.IsNullOrWhiteSpace(analysis.Summary))
        {
            parts.Add("ملخص مختصر: " + analysis.Summary);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static IReadOnlyList<FatwaCandidate> OrderCandidates(
        IReadOnlyList<FatwaCandidate> candidates,
        IReadOnlyList<long> rerankedIds)
    {
        if (rerankedIds.Count == 0)
        {
            // Fall back to vector order (as returned from repository).
            return candidates;
        }

        var byId = candidates.ToDictionary(c => c.FatwaId);
        var ordered = new List<FatwaCandidate>(candidates.Count);

        // First: add those explicitly ranked by LLM, in order.
        foreach (var id in rerankedIds)
        {
            if (byId.TryGetValue(id, out var candidate))
            {
                ordered.Add(candidate);
                byId.Remove(id);
            }
        }

        // Then: append any remaining candidates in their original order.
        foreach (var candidate in candidates)
        {
            if (byId.ContainsKey(candidate.FatwaId))
            {
                ordered.Add(candidate);
                byId.Remove(candidate.FatwaId);
            }
        }

        return ordered;
    }
}

