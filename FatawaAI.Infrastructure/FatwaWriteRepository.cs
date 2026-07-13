using System.Data;
using Dapper;
using FatawaAI.Core;
using Npgsql;

namespace FatawaAI.Infrastructure;

/// <summary>
/// Basic PostgreSQL implementation of IFatwaWriteRepository.
/// Uses batched INSERT ... ON CONFLICT for simplicity.
/// </summary>
public sealed class FatwaWriteRepository : IFatwaWriteRepository
{
    private readonly string _connectionString;

    public FatwaWriteRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private IDbConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task BulkUpsertAsync(IEnumerable<FatwaWithEmbedding> fatwas, CancellationToken cancellationToken)
    {
        const string sql = @"
INSERT INTO fatwas (
    collection_type,
    source_id,
    title,
    question,
    answer,
    source_url,
    audio_url,
    categories,
    embedding_title,
    embedding_question
) VALUES (
    @CollectionType,
    @SourceId,
    @Title,
    @Question,
    @Answer,
    @SourceUrl,
    @AudioUrl,
    @Categories,
    @EmbeddingTitle::vector,
    @EmbeddingQuestion::vector
)
ON CONFLICT (collection_type, source_id) DO UPDATE SET
    title      = EXCLUDED.title,
    question   = EXCLUDED.question,
    answer     = EXCLUDED.answer,
    source_url = EXCLUDED.source_url,
    audio_url  = EXCLUDED.audio_url,
    categories = EXCLUDED.categories,
    embedding_title    = EXCLUDED.embedding_title,
    embedding_question = EXCLUDED.embedding_question;
";

        using var connection = CreateConnection();
        // Dapper doesn't support CancellationToken directly for ExecuteAsync, but operations are fast for ~20k rows.
        await connection.ExecuteAsync(sql, fatwas);
    }
}


