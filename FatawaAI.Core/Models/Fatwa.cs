using MediatR;

namespace FatawaAI.Core;

/// <summary>
/// Core representation of a fatwa as stored in PostgreSQL.
/// Definitions only; implementations live in Domain/Infrastructure.
/// </summary>
public sealed record Fatwa
{
    public long FatwaId { get; init; }
    public string CollectionType { get; init; } = string.Empty;
    public int SourceId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public string? AudioUrl { get; init; }
    public string[] Categories { get; init; } = Array.Empty<string>();

    public Fatwa() { }

    public Fatwa(
        long fatwaId,
        string collectionType,
        int sourceId,
        string title,
        string question,
        string answer,
        string sourceUrl,
        string? audioUrl,
        string[] categories)
    {
        FatwaId = fatwaId;
        CollectionType = collectionType;
        SourceId = sourceId;
        Title = title;
        Question = question;
        Answer = answer;
        SourceUrl = sourceUrl;
        AudioUrl = audioUrl;
        Categories = categories;
    }
}


