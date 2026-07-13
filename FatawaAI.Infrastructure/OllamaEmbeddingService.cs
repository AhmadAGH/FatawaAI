using System.Net.Http.Json;
using FatawaAI.Core;
using Microsoft.Extensions.Options;

namespace FatawaAI.Infrastructure;

public sealed class OllamaEmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaEmbeddingService(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    private sealed record EmbeddingRequest(string model, string input);

    private sealed record EmbeddingResponse(float[][] embeddings);

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        var request = new EmbeddingRequest(_options.EmbeddingModel, text);

        var response = await _httpClient.PostAsJsonAsync(
            $"{_options.BaseUrl}/api/embed",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
            cancellationToken: cancellationToken);

        if (payload?.embeddings is null || payload.embeddings.Length == 0)
        {
            throw new InvalidOperationException("Ollama returned no embeddings.");
        }

        return payload.embeddings[0];
    }
}


