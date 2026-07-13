using System.Data;
using Dapper;
using FatawaAI.Core;
using Npgsql;

namespace FatawaAI.Infrastructure;

/// <summary>
/// Basic PostgreSQL implementation of IFatwaReadRepository using Dapper.
/// </summary>
public sealed class FatwaReadRepository : IFatwaReadRepository
{
    private readonly string _connectionString;

    public FatwaReadRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    // Internal classes for hybrid search results
    private sealed class VectorSearchResult
    {
        public long FatwaId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string QuestionSnippet { get; set; } = string.Empty;
        public string AnswerSnippet { get; set; } = string.Empty;
        public double VectorScore { get; set; }
        public long VectorRank { get; set; }
    }

    private sealed class FtsSearchResult
    {
        public long FatwaId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string QuestionSnippet { get; set; } = string.Empty;
        public string AnswerSnippet { get; set; } = string.Empty;
        public double FtsScore { get; set; }
        public long FtsRank { get; set; }
    }

    public async Task<Fatwa?> GetByIdAsync(long fatwaId, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT fatwa_id     AS FatwaId,
       collection_type AS CollectionType,
       source_id    AS SourceId,
       title,
       question,
       answer,
       source_url   AS SourceUrl,
       audio_url    AS AudioUrl,
       categories   AS Categories
FROM fatwas
WHERE fatwa_id = @Id;
";

        using var connection = CreateConnection();
        var result = await connection.QuerySingleOrDefaultAsync<Fatwa>(sql, new { Id = fatwaId });
        return result;
    }

    public async Task<IReadOnlyList<Fatwa>> GetByIdsAsync(
        IReadOnlyCollection<long> fatwaIds,
        CancellationToken cancellationToken)
    {
        if (fatwaIds.Count == 0)
        {
            return Array.Empty<Fatwa>();
        }

        const string sql = @"
SELECT fatwa_id     AS FatwaId,
       collection_type AS CollectionType,
       source_id    AS SourceId,
       title,
       question,
       answer,
       source_url   AS SourceUrl,
       audio_url    AS AudioUrl,
       categories   AS Categories
FROM fatwas
WHERE fatwa_id = ANY(@Ids);
";

        using var connection = CreateConnection();
        var result = await connection.QueryAsync<Fatwa>(sql, new { Ids = fatwaIds.ToArray() });
        return result.ToList();
    }

    public async Task<IReadOnlyList<string>> GetAllCategoriesAsync(CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT DISTINCT unnest(categories) AS category
FROM fatwas
ORDER BY category;
";

        using var connection = CreateConnection();
        var result = await connection.QueryAsync<string>(sql);
        return result.ToList();
    }

    public async Task<IReadOnlyList<FatwaCandidate>> SearchByEmbeddingAsync(
        float[] embedding,
        int limit,
        string? category,
        string? collectionType,
        CancellationToken cancellationToken)
    {
        // Base vector search query with optional filters.
        var sql = @"
SELECT fatwa_id     AS FatwaId,
       title,
       left(question, 400) AS QuestionSnippet,
       left(answer, 400)   AS AnswerSnippet,
       (embedding <=> @Embedding::vector) AS VectorScore
FROM fatwas
WHERE embedding IS NOT NULL
";

        if (!string.IsNullOrWhiteSpace(category))
        {
            sql += "  AND @Category = ANY(categories)\n";
        }

        if (!string.IsNullOrWhiteSpace(collectionType))
        {
            sql += "  AND collection_type = @CollectionType\n";
        }

        sql += @"
ORDER BY embedding <=> @Embedding::vector
LIMIT @Limit;
";

        using var connection = CreateConnection();

        // Npgsql maps float[] to PostgreSQL real[] automatically.
        var result = await connection.QueryAsync<FatwaCandidate>(sql, new
        {
            Embedding = embedding,
            Category = category,
            CollectionType = collectionType,
            Limit = limit
        });

        return result.ToList();
    }

    public async Task<IReadOnlyList<FatwaCandidate>> SearchByEmbeddingPerFieldAsync(
        float[] queryEmbedding,
        string queryText,
        SearchWeights weights,
        int limit,
        string? category,
        string? collectionType,
        CancellationToken cancellationToken)
    {
        var sql = @"
SELECT fatwa_id     AS FatwaId,
       title,
       left(question, 400) AS QuestionSnippet,
       left(answer, 400)   AS AnswerSnippet,
       ( @TitleW    * (embedding_title    <=> @Embedding::vector)
       + @QuestionW * (embedding_question <=> @Embedding::vector)
       ) AS VectorScore,
       ts_rank_cd(to_tsvector('arabic', title || ' ' || question), plainto_tsquery('arabic', @QueryText)) AS FtsRank
FROM fatwas
WHERE embedding_title IS NOT NULL
  AND embedding_question IS NOT NULL
";

        if (!string.IsNullOrWhiteSpace(category))
        {
            sql += "  AND @Category = ANY(categories)\n";
        }

        if (!string.IsNullOrWhiteSpace(collectionType))
        {
            sql += "  AND collection_type = @CollectionType\n";
        }

        sql += @"
ORDER BY VectorScore, FtsRank DESC
LIMIT @Limit;
";

        using var connection = CreateConnection();

        var result = await connection.QueryAsync<FatwaCandidate>(sql, new
        {
            Embedding = queryEmbedding,
            QueryText = queryText,
            TitleW = weights.TitleWeight,
            QuestionW = weights.QuestionWeight,
            Category = category,
            CollectionType = collectionType,
            Limit = limit
        });

        return result.ToList();
    }

    public async Task<IReadOnlyList<FatwaCandidate>> SearchHybridAsync(
        float[] queryEmbedding,
        string queryText,
        SearchWeights weights,
        int limit,
        string? category,
        string? collectionType,
        CancellationToken cancellationToken)
    {
        // Reciprocal Rank Fusion (RRF) combines vector and full-text search
        // RRF score = sum(1 / (k + rank)) where k=60 is a constant
        const int k = 60;
        const int vectorLimit = 100;
        const int ftsLimit = 50;

        // 1. Get vector search results
        var vectorSql = @"
SELECT fatwa_id     AS FatwaId,
       title,
       left(question, 400) AS QuestionSnippet,
       left(answer, 400)   AS AnswerSnippet,
       ( @TitleW    * (embedding_title    <=> @Embedding::vector)
       + @QuestionW * (embedding_question <=> @Embedding::vector)
       ) AS VectorScore,
       ROW_NUMBER() OVER (ORDER BY 
           @TitleW * (embedding_title <=> @Embedding::vector) +
           @QuestionW * (embedding_question <=> @Embedding::vector)
       ) AS VectorRank
FROM fatwas
WHERE embedding_title IS NOT NULL
  AND embedding_question IS NOT NULL
";

        if (!string.IsNullOrWhiteSpace(category))
        {
            vectorSql += "  AND @Category = ANY(categories)\n";
        }

        if (!string.IsNullOrWhiteSpace(collectionType))
        {
            vectorSql += "  AND collection_type = @CollectionType\n";
        }

        vectorSql += @"
ORDER BY VectorScore
LIMIT @VectorLimit;
";

        // 2. Get full-text search results (using pre-computed search_tsv column with GIN index)
        var ftsSql = @"
SELECT fatwa_id     AS FatwaId,
       title,
       left(question, 400) AS QuestionSnippet,
       left(answer, 400)   AS AnswerSnippet,
       ts_rank_cd(search_tsv, plainto_tsquery('arabic', @QueryText)) AS FtsScore,
       ROW_NUMBER() OVER (ORDER BY ts_rank_cd(search_tsv, plainto_tsquery('arabic', @QueryText)) DESC) AS FtsRank
FROM fatwas
WHERE search_tsv @@ plainto_tsquery('arabic', @QueryText)
";

        if (!string.IsNullOrWhiteSpace(category))
        {
            ftsSql += "  AND @Category = ANY(categories)\n";
        }

        if (!string.IsNullOrWhiteSpace(collectionType))
        {
            ftsSql += "  AND collection_type = @CollectionType\n";
        }

        ftsSql += @"
ORDER BY FtsScore DESC
LIMIT @FtsLimit;
";

        var parameters = new
        {
            Embedding = queryEmbedding,
            QueryText = queryText,
            TitleW = weights.TitleWeight,
            QuestionW = weights.QuestionWeight,
            Category = category,
            CollectionType = collectionType,
            VectorLimit = vectorLimit,
            FtsLimit = ftsLimit
        };

        // Execute both queries in parallel
        using (var connection = CreateConnection())
        {
            connection.Open();

            var vectorResults = (await connection.QueryAsync<VectorSearchResult>(vectorSql, parameters)).ToList();
            var ftsResults = (await connection.QueryAsync<FtsSearchResult>(ftsSql, parameters)).ToList();

            // 3. Apply Reciprocal Rank Fusion
            var rrfScores = new Dictionary<long, double>();
            var candidateData = new Dictionary<long, FatwaCandidate>();

            // Process vector results
            foreach (var row in vectorResults)
            {
                double rrfScore = 1.0 / (k + row.VectorRank);

                rrfScores[row.FatwaId] = rrfScore;
                candidateData[row.FatwaId] = new FatwaCandidate
                {
                    FatwaId = row.FatwaId,
                    Title = row.Title,
                    QuestionSnippet = row.QuestionSnippet,
                    AnswerSnippet = row.AnswerSnippet,
                    VectorScore = row.VectorScore
                };
            }

            // Process FTS results and add to RRF scores
            foreach (var row in ftsResults)
            {
                double rrfScore = 1.0 / (k + row.FtsRank);

                if (rrfScores.ContainsKey(row.FatwaId))
                {
                    rrfScores[row.FatwaId] += rrfScore;
                }
                else
                {
                    rrfScores[row.FatwaId] = rrfScore;
                    candidateData[row.FatwaId] = new FatwaCandidate
                    {
                        FatwaId = row.FatwaId,
                        Title = row.Title,
                        QuestionSnippet = row.QuestionSnippet,
                        AnswerSnippet = row.AnswerSnippet,
                        VectorScore = 0.0 // FTS-only result
                    };
                }
            }

            // 4. Sort by RRF score and return top results
            var rankedResults = rrfScores
                .OrderByDescending(kvp => kvp.Value)
                .Take(limit)
                .Select(kvp => candidateData[kvp.Key])
                .ToList();

            return rankedResults;
        }
            
    }
}

