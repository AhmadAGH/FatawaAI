namespace FatawaAI.Core;

public interface IQueryAnalysisService
{
    Task<QueryAnalysisResult?> AnalyzeAsync(string userQuery, CancellationToken cancellationToken);
}

