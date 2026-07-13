namespace FatawaAI.Core;

/// <summary>
/// Candidate fatwa returned from the initial vector search.
/// </summary>
public sealed record FatwaCandidate
{
    public FatwaCandidate()
    {
        Title = string.Empty;
        QuestionSnippet = string.Empty;
        AnswerSnippet = string.Empty;
    }

    public FatwaCandidate(
        long fatwaId,
        string title,
        string questionSnippet,
        string answerSnippet,
        double? vectorScore)
    {
        FatwaId = fatwaId;
        Title = title;
        QuestionSnippet = questionSnippet;
        AnswerSnippet = answerSnippet;
        VectorScore = vectorScore;
    }

    public long FatwaId { get; init; }
    public string Title { get; init; }
    public string QuestionSnippet { get; init; }
    public string AnswerSnippet { get; init; }
    public double? VectorScore { get; init; }
}


