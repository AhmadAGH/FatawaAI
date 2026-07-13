using System.Net.Http.Json;
using System.Text.Json;
using FatawaAI.Core;
using Microsoft.Extensions.Options;

namespace FatawaAI.Infrastructure;

public sealed class OllamaQueryAnalysisService : IQueryAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaQueryAnalysisService(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    private sealed record ChatMessage(string role, string content);

    private sealed record ChatOptions(int num_ctx);
    private sealed record ChatRequest(string model, ChatMessage[] messages, double temperature, bool stream, string format, ChatOptions options);

    private sealed record ChatResponseMessage(string role, string content);

    private sealed record ChatResponse(ChatResponseMessage message);

    public async Task<QueryAnalysisResult?> AnalyzeAsync(string userQuery, CancellationToken cancellationToken)
    {
        var systemPrompt =
"You are an automated Islamic question analyzer for a fatwa search engine, NOT a mufti.\n" +
"Your ONLY task is to extract structured information from the Arabic question.\n" +
"\n" +
"🚫 STRICTLY FORBIDDEN:\n" +
"- Issuing fatwas, rulings, or religious advice\n" +
"- Interpreting Quran/Hadith or mentioning evidence\n" +
"- Adding information not explicitly in the question\n" +
"- Any content resembling religious guidance\n" +
"\n" +
"✅ EXTRACT ONLY:\n" +
"1. fiqh_topic: SPECIFIC category name (1-3 Arabic words) that matches 5-20 relevant fatwas\n" +
"2. keywords: 5-8 words including BOTH specific terms AND underlying concepts\n" +
"3. summary: 1-sentence Arabic rephrasing that includes the underlying concept\n" +
"\n" +
"🔑 CRITICAL: Extract UNDERLYING CONCEPTS for modern terms:\n" +
"- Modern services/products → Traditional fiqh concepts\n" +
"- Example: \"تابي\" or \"تمارا\" → \"بيع بالتقسيط\" + \"رسوم تأخير\" + \"قرض استهلاكي\"\n" +
"- Example: \"بطاقة ائتمان\" → \"قرض ربوي\" + \"فوائد\" + \"دين\"\n" +
"\n" +
"📋 EXAMPLES (follow exact pattern):\n" +
"Input: \"حكم الشراء باستخدام تابي و تمارا\"\n" +
"Output: { \"fiqh_topic\": \"بيع بالتقسيط\", \"keywords\": [\"تابي\", \"تمارا\", \"شراء\", \"تقسيط\", \"رسوم\", \"تأخير\", \"قرض\", \"استهلاكي\"], \"summary\": \"حكم الشراء بالتقسيط مع رسوم عند التأخير (تابي وتمارا)\" }\n" +
"\n" +
"Input: \"حكم القرض من البنك\"\n" +
"Output: { \"fiqh_topic\": \"قروض ربوية\", \"keywords\": [\"قرض\", \"بنك\", \"ربا\", \"فائدة\", \"دين\"], \"summary\": \"حكم أخذ القرض الربوي من البنك\" }\n" +
"\n" +
"Input: \"حكم الصلاة في المنزل\"\n" +
"Output: { \"fiqh_topic\": \"صلاة الجماعة\", \"keywords\": [\"صلاة\", \"منزل\", \"جماعة\", \"مسجد\", \"ترك\"], \"summary\": \"حكم صلاة الرجل في المنزل وترك الجماعة\" }\n" +
"\n" +
"⚠️ KEY RULES:\n" +
"- fiqh_topic must be SPECIFIC (not broad like \"معاملات\")\n" +
"- keywords: Include BOTH exact terms AND related concepts (5-8 words)\n" +
"- summary: Must capture the underlying fiqh concept, not just repeat the question\n" +
"- For modern terms: Think \"What traditional ruling applies here?\"\n" +
"- If unclear: { \"fiqh_topic\": \"\", \"keywords\": [], \"summary\": \"\" }\n" +
"\n" +
"📦 MANDATORY JSON OUTPUT (NO EXTRA TEXT):\n" +
"{ \"fiqh_topic\": \"...\", \"keywords\": [\"...\", \"...\", ...], \"summary\": \"...\" }";




        var request = new ChatRequest(
            _options.ChatModel,
            new[]
            {
                new ChatMessage("system", systemPrompt),
                new ChatMessage("user", userQuery)
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

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        ChatResponse? payload = null;
        try
        {
            payload = JsonSerializer.Deserialize<ChatResponse>(raw);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Query analysis deserialize error: {ex.Message}");
            return null;
        }

        if (payload?.message?.content is null)
        {
            return null;
        }

        try
        {
            var cleaned = ExtractJsonObject(payload.message.content);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(cleaned);
            var root = doc.RootElement;

            var fiqhTopic = root.TryGetProperty("fiqh_topic", out var topicEl)
                ? topicEl.GetString()
                : null;

            var keywords = new List<string>();
            if (root.TryGetProperty("keywords", out var kwEl) && kwEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in kwEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            keywords.Add(value);
                        }
                    }
                }
            }

            var summary = root.TryGetProperty("summary", out var summaryEl)
                ? summaryEl.GetString()
                : null;

            return new QueryAnalysisResult(
                FiqhTopic: fiqhTopic ?? string.Empty,
                Keywords: keywords,
                Summary: summary ?? string.Empty);
        }
        catch (Exception ex)
        {
            // If parsing fails, fall back to null (handler will degrade gracefully).
            Console.WriteLine($"Query analysis parse error: {ex.Message}");
            return null;
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


