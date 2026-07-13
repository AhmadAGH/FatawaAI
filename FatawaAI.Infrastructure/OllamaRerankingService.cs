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

    private sealed record ChatRequest(string model, ChatMessage[] messages, double temperature, bool stream, string format);

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
@"You are an automated fatwa ranking system. Your ONLY task is to rank fatwas by relevance to the user's question.

🚫 STRICTLY FORBIDDEN:
- Do NOT provide religious advice, interpretations, or explanations
- Do NOT add any text before or after the JSON output
- Do NOT use markdown code blocks (no ```json)
- Do NOT have any conversational responses

✅ MANDATORY OUTPUT FORMAT (JSON ONLY):
{
  ""ranked_fatwa_ids"": [array of fatwa IDs, ordered from most to least relevant]
}

📋 RANKING CRITERIA (in priority order):
1. EXACT MATCH: Fatwa directly answers the exact question
2. SPECIFICITY: Fatwa is specific, not general (e.g., ""Tabby installments"" > ""loans in general"")
3. COMPLETENESS: Fatwa covers all aspects of the question
4. CLARITY: Fatwa provides clear and direct answer

⚠️ CRITICAL RULES:
- Output MUST start with { and end with }
- NO text before the JSON
- NO text after the JSON
- NO explanations or commentary
- ONLY valid JSON format

Example of CORRECT output:
{""ranked_fatwa_ids"":[1,5,3,2]}

Example of WRONG output (will cause system failure):
Here is the ranking: {""ranked_fatwa_ids"":[1,5,3,2]}

⚠️ FINAL WARNING: Any text outside JSON will fail the system. Output JSON ONLY.";



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
            format: "json"  // FORCE JSON output mode
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


