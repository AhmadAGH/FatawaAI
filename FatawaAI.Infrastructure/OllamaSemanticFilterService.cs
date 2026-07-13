using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FatawaAI.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FatawaAI.Infrastructure;

public sealed class OllamaSemanticFilterService : ISemanticFilterService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaSemanticFilterService> _logger;

    public OllamaSemanticFilterService(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaSemanticFilterService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private sealed record ChatMessage(string role, string content);
    private sealed record ChatRequest(string model, ChatMessage[] messages, double temperature, bool stream, string format, ChatOptions options);
    private sealed record ChatOptions(int num_ctx);

    private sealed record ChatResponseMessage(string role, string content);
    private sealed record ChatResponse(ChatResponseMessage message);

    public async Task<IReadOnlyList<FatwaCandidate>> FilterAsync(
        string userQuery,
        IReadOnlyList<FatwaCandidate> candidates,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("========== FilterAsync START ==========");
        _logger.LogInformation("User Query: {Query}", userQuery);
        _logger.LogInformation("Number of candidates: {Count}", candidates.Count);

        if (candidates.Count == 0)
        {
            _logger.LogWarning("No candidates provided, returning empty list");
            return Array.Empty<FatwaCandidate>();
        }

        var systemPrompt =
@"You are an automated fatwa classification system. Your ONLY task is to classify fatwas by their topical relevance to the user's question.

🚫 STRICTLY FORBIDDEN:
- Do NOT provide religious advice, interpretations, or explanations
- Do NOT add any text before or after the JSON output
- Do NOT use markdown code blocks (no ```json)
- Do NOT have conversational responses

📋 CLASSIFICATION CATEGORIES:
- RELEVANT: Fatwa directly addresses the same specific topic/ruling as the question
- PARTIAL: Fatwa is related but broader or narrower in scope (same jurisprudence area but not specific)
- IRRELEVANT: Fatwa is about a completely different topic (different jurisprudence area)

🔍 CLASSIFICATION EXAMPLES:
Question: ""Ruling on buying using Tabby and Tamara installment services""
- RELEVANT: Fatwa about installment sales, consumer loans, specifically Tabby/Tamara
- PARTIAL: General fatwa about usury in transactions, deferred payment sales
- IRRELEVANT: Fatwa about prayer, zakat, marriage, etc.

Question: ""Ruling on praying Friday prayer at home""
- RELEVANT: Fatwa about Friday prayer at home, ruling on missing Friday prayer
- PARTIAL: General fatwa about congregational prayer, virtue of Friday
- IRRELEVANT: Fatwa about fasting, hajj, financial transactions

✅ MANDATORY OUTPUT FORMAT (JSON ONLY):
{
  ""classifications"": [
    { ""fatwa_id"": 123, ""classification"": ""RELEVANT"" },
    { ""fatwa_id"": 456, ""classification"": ""PARTIAL"" },
    { ""fatwa_id"": 789, ""classification"": ""IRRELEVANT"" }
  ]
}

⚠️ CRITICAL: Output MUST be valid JSON only. NO text before or after.";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("User Question:");
        sb.AppendLine(userQuery);
        sb.AppendLine();
        sb.AppendLine("Fatwas to Classify:");
        sb.AppendLine();

        foreach (var c in candidates)
        {
            sb.AppendLine($"- Fatwa ID: {c.FatwaId}");
            sb.AppendLine($"  Title: {c.Title}");
            sb.AppendLine($"  Question Snippet: {c.QuestionSnippet}");
            sb.AppendLine($"  Answer Snippet: {c.AnswerSnippet}");
            sb.AppendLine();
        }

        var userContent = sb.ToString();

        _logger.LogInformation("--- PROMPT TO LLM ---");
        _logger.LogInformation("System Prompt Length: {Length} chars", systemPrompt.Length);
        _logger.LogInformation("User Content:\n{Content}", userContent);

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
            options: new ChatOptions(num_ctx: 8192)  // NEW: avoid Ollama's 2048-token default truncating the prompt
        );

        _logger.LogInformation("Making HTTP request to Ollama at: {Url}", $"{_options.BaseUrl}/api/chat");
        _logger.LogInformation("Using model: {Model}", _options.ChatModel);

        var httpRequest = new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(
            $"{_options.BaseUrl}/api/chat",
            httpRequest,
            cancellationToken);

        response.EnsureSuccessStatusCode();
        _logger.LogInformation("HTTP request successful, status: {Status}", response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogInformation("--- RAW LLM RESPONSE ---");
        _logger.LogInformation("{Raw}", raw);

        ChatResponse? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<ChatResponse>(raw);
            _logger.LogInformation("Successfully deserialized ChatResponse");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic filter deserialize error: {Message}", ex.Message);
            // On error, return all candidates (fail open)
            return candidates;
        }

        if (payload?.message?.content is null)
        {
            _logger.LogWarning("Payload or message content is null, returning all candidates");
            return candidates;
        }

        _logger.LogInformation("--- LLM CONTENT (from message) ---");
        _logger.LogInformation("{Content}", payload.message.content);

        Console.WriteLine("========== SEMANTIC FILTER LLM RAW RESPONSE ==========");
        Console.WriteLine(payload.message.content);
        Console.WriteLine("========== END RAW RESPONSE ==========");

        try
        {
            var cleaned = ExtractJsonObject(payload.message.content);
            _logger.LogInformation("--- CLEANED JSON ---");
            _logger.LogInformation("{Cleaned}", cleaned);

            if (string.IsNullOrWhiteSpace(cleaned))
            {
                _logger.LogWarning("Cleaned JSON is null or empty, returning all candidates");
                return candidates;
            }

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            if (!root.TryGetProperty("classifications", out var classificationsEl) ||
                classificationsEl.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning("No 'classifications' array found in response, returning all candidates");
                return candidates;
            }

            var classifications = new Dictionary<long, string>();
            _logger.LogInformation("--- PARSING CLASSIFICATIONS ---");

            foreach (var item in classificationsEl.EnumerateArray())
            {
                if (item.TryGetProperty("fatwa_id", out var idEl) &&
                    item.TryGetProperty("classification", out var classEl) &&
                    idEl.TryGetInt64(out var fatwaId))
                {
                    var classification = classEl.GetString();
                    if (!string.IsNullOrWhiteSpace(classification))
                    {
                        var upperClass = classification.ToUpperInvariant();
                        classifications[fatwaId] = upperClass;
                        _logger.LogInformation("Fatwa {Id}: {Classification}", fatwaId, upperClass);
                    }
                }
            }

            _logger.LogInformation("Total classifications parsed: {Count}", classifications.Count);

            // Filter: keep RELEVANT and PARTIAL, discard IRRELEVANT
            _logger.LogInformation("--- FILTERING RESULTS ---");
            var filtered = new List<FatwaCandidate>();
            foreach (var candidate in candidates)
            {
                if (classifications.TryGetValue(candidate.FatwaId, out var classification))
                {
                    if (classification == "RELEVANT" || classification == "PARTIAL")
                    {
                        _logger.LogInformation("KEEP Fatwa {Id} ({Title}) - {Classification}",
                            candidate.FatwaId, candidate.Title, classification);
                        filtered.Add(candidate);
                    }
                    else
                    {
                        _logger.LogInformation("DROP Fatwa {Id} ({Title}) - {Classification}",
                            candidate.FatwaId, candidate.Title, classification);
                    }
                }
                else
                {
                    // If LLM didn't classify this candidate, keep it (fail open)
                    _logger.LogWarning("KEEP Fatwa {Id} ({Title}) - NOT CLASSIFIED (fail-open)",
                        candidate.FatwaId, candidate.Title);
                    filtered.Add(candidate);
                }
            }

            _logger.LogInformation("========== FilterAsync END ==========");
            _logger.LogInformation("Input: {Input} candidates, Output: {Output} candidates",
                candidates.Count, filtered.Count);

            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Semantic filter parse error: {Message}", ex.Message);
            // On error, return all candidates (fail open)
            return candidates;
        }
    }

    private static string? ExtractJsonObject(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            return null;
        }

        var slice = content.Substring(start, end - start + 1).Trim();

        if (slice.StartsWith("```"))
        {
            var innerStart = slice.IndexOf('{');
            var innerEnd = slice.LastIndexOf('}');
            if (innerStart >= 0 && innerEnd >= innerStart)
            {
                slice = slice.Substring(innerStart, innerEnd - innerStart + 1).Trim();
            }
        }

        return slice;
    }
}

