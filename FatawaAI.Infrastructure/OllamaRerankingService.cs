using System.Net.Http.Json;
using System.Text.Json;
using FatawaAI.Core;
using Microsoft.Extensions.Options;

namespace FatawaAI.Infrastructure;

public sealed class OllamaRerankingService : IRerankingService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaRerankingService(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    private sealed record ChatMessage(string role, string content);

    private sealed record ChatOptions(int num_ctx);
    private sealed record ChatRequest(string model, ChatMessage[] messages, double temperature, bool stream, string format, ChatOptions options);

    private sealed record ChatResponseMessage(string role, string content);

    private sealed record ChatResponse(ChatResponseMessage message);

    public async Task<IReadOnlyList<long>> RerankAsync(
        string userQuery,
        IReadOnlyList<FatwaCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0)
        {
            return Array.Empty<long>();
        }

        var systemPrompt =
@"You are an automated fatwa ranking system. Rank fatwas by relevance to the user's question.

Do not provide religious advice, interpretations, or explanations — only rank.

RANKING CRITERIA (in priority order):
1. Exact match: directly answers the question
2. Specificity: specific over general (e.g. ""Tabby installments"" over ""loans in general"")
3. Completeness: covers all aspects of the question
4. Clarity: clear, direct answer

Output JSON only:
{""ranked_fatwa_ids"": [array of fatwa IDs, most to least relevant]}";



        var sb = new System.Text.StringBuilder();
        sb.AppendLine("User Question:");
        sb.AppendLine(userQuery);
        sb.AppendLine();
        sb.AppendLine("Fatwa Candidates to Rank:");

        foreach (var c in candidates)
        {
            sb.AppendLine($"- Fatwa ID: {c.FatwaId}");
            sb.AppendLine($"  Title: {c.Title}");
            sb.AppendLine($"  Question Snippet: {c.QuestionSnippet}");
            sb.AppendLine($"  Answer Snippet: {c.AnswerSnippet}");
            sb.AppendLine();
        }

        var userContent = sb.ToString();

        var request = new ChatRequest(
            _options.ChatModel,
            new[]
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userContent)
            },
            temperature: 0.0,
            stream: false,
            format: "json",
            options: new ChatOptions(num_ctx: 8192)
        );

        var response = await _httpClient.PostAsJsonAsync(
            $"{_options.BaseUrl}/api/chat",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<ChatResponse>(
            cancellationToken: cancellationToken);

        if (payload?.message?.content is null)
        {
            return Array.Empty<long>();
        }

        try
        {
            var content = payload.message.content.Trim();
            
            // Try to extract JSON if LLM added extra text
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                content = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }
            
            // Remove markdown code blocks if present
            content = content.Replace("```json", "").Replace("```", "").Trim();
            
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ranked_fatwa_ids", out var idsEl) ||
                idsEl.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<long>();
            }

            var result = new List<long>();
            foreach (var item in idsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number &&
                    item.TryGetInt64(out var id))
                {
                    result.Add(id);
                }
            }

            return result;
        }
        catch
        {
            // Log the error content for debugging
            System.Console.WriteLine($"Failed to parse reranking response: {payload.message.content}");
            return Array.Empty<long>();
        }
    }
}


