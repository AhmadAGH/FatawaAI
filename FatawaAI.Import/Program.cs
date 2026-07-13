using System.Text.Json;
using FatawaAI.Core;
using FatawaAI.Infrastructure;

// Simple import tool for pre-scraped fatwas JSON into PostgreSQL.
// Usage (from solution root):
//   dotnet run --project FatawaAI.Import

internal sealed record PreScrapedFatwa(
    int Id,
    string Question,
    string Title,
    string Answer,
    string Link,
    string? Audio,
    string[]? Categories);

internal static class Program
{
    // Chunking settings for embedding to avoid context overflows.
    private const int ChunkSize = 1800; // characters per chunk (conservative vs 8192-token limit)
    private const int MaxChunks = 12;   // safety guard to avoid huge inputs
    private const int MaxParallel = 20;  // control parallel embedding to avoid overloading Ollama

    public static async Task Main()
    {
        var rootPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "pre-scaraped_fatawa");

        if (!Directory.Exists(rootPath))
        {
            Console.WriteLine($"Data directory not found: {rootPath}");
            return;
        }

        // Connection string and Ollama URL should match the API project for consistency.
        var connectionString =
            Environment.GetEnvironmentVariable("FATAWA_POSTGRES") ??
            "Host=localhost;Port=5432;Database=fatawa;Username=postgres;Password=postgres";

        var ollamaBaseUrl =
            Environment.GetEnvironmentVariable("FATAWA_OLLAMA_URL") ??
            "http://localhost:11434";

        var ollamaOptions = new OllamaOptions
        {
            BaseUrl = ollamaBaseUrl,
            EmbeddingModel = "nomic-embed-text",
            ChatModel = "llama3.1:8b"
        };

        var httpClient = new HttpClient();
        var embeddingService = new OllamaEmbeddingService(httpClient,
            Microsoft.Extensions.Options.Options.Create(ollamaOptions));
        var writeRepository = new FatwaWriteRepository(connectionString);

        Console.WriteLine("Starting import from JSON files...");

        var files = new[]
        {
            "fatawaa_aldurus.json",
            "fatawaa_aljamie_alkabir.json",
            "nur_ealaa_aldarb.json"
        };

        var allFatwas = new List<FatwaWithEmbedding>(capacity: 20000);
        var skipped = new List<(string Collection, int SourceId, string Reason)>();

        foreach (var fileName in files)
        {
            var path = Path.Combine(rootPath, fileName);

            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found, skipping: {path}");
                continue;
            }

            Console.WriteLine($"Reading {path}...");

            await using var stream = File.OpenRead(path);

            var jsonFatwas = await JsonSerializer.DeserializeAsync<List<PreScrapedFatwa>>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (jsonFatwas is null)
            {
                Console.WriteLine($"No data in {path}, skipping.");
                continue;
            }

            var collectionType = Path.GetFileNameWithoutExtension(fileName) ?? string.Empty;
            var bag = new System.Collections.Concurrent.ConcurrentBag<FatwaWithEmbedding>();
            var skippedBag = new System.Collections.Concurrent.ConcurrentBag<(string Collection, int SourceId, string Reason)>();
            int prepared = 0;

            await Parallel.ForEachAsync(jsonFatwas, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel }, async (f, ct) =>
            {
                var categories = f.Categories ?? Array.Empty<string>();

                var textTitle = f.Title ?? string.Empty;
                var textQuestion = f.Question ?? string.Empty;

                float[] embTitle;
                float[] embQuestion;
                try
                {
                    embTitle = await embeddingService.EmbedAsync(textTitle, ct);
                    embQuestion = await embeddingService.EmbedAsync(textQuestion, ct);
                }
                catch (Exception ex)
                {
                    skippedBag.Add((collectionType, f.Id, $"embed_error: {ex.Message}"));
                    return;
                }

                var fatwa = new FatwaWithEmbedding(
                    CollectionType: collectionType,
                    SourceId: f.Id,
                    Title: f.Title ?? string.Empty,
                    Question: f.Question ?? string.Empty,
                    Answer: f.Answer ?? string.Empty,
                    SourceUrl: f.Link,
                    AudioUrl: f.Audio,
                    Categories: categories,
                    EmbeddingTitle: embTitle,
                    EmbeddingQuestion: embQuestion);

                bag.Add(fatwa);

                var count = Interlocked.Increment(ref prepared);
                if (count % 200 == 0)
                {
                    Console.WriteLine($"Prepared {count} fatwas so far...");
                }
            });

            allFatwas.AddRange(bag);
            foreach (var s in skippedBag)
            {
                skipped.Add(s);
            }
        }

        if (allFatwas.Count == 0)
        {
            Console.WriteLine("No fatwas to import.");
        }
        else
        {
            Console.WriteLine($"Upserting {allFatwas.Count} fatwas into PostgreSQL...");
            await writeRepository.BulkUpsertAsync(allFatwas, CancellationToken.None);
            Console.WriteLine("Import completed.");
        }

        if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Skipped {skipped.Count} fatwas due to embedding issues:");
            foreach (var s in skipped.Take(20))
            {
                Console.WriteLine($" - {s.Collection}/{s.SourceId} reason={s.Reason}");
            }
            if (skipped.Count > 20)
            {
                Console.WriteLine($" ... and {skipped.Count - 20} more.");
            }
            var reportPath = Path.Combine(AppContext.BaseDirectory, "skipped_fatwas.txt");
            await File.WriteAllLinesAsync(reportPath,
                skipped.Select(s => $"{s.Collection},{s.SourceId},{s.Reason}"));
            Console.WriteLine($"Full list written to: {reportPath}");
        }
    }

    private static List<string> ChunkText(string text, int chunkSize, int maxChunks)
    {
        var chunks = new List<string>();
        for (int i = 0; i < text.Length && chunks.Count < maxChunks; i += chunkSize)
        {
            var len = Math.Min(chunkSize, text.Length - i);
            chunks.Add(text.Substring(i, len));
        }
        return chunks;
    }

    private static async Task<float[]> EmbedAndAverageAsync(IEmbeddingService embeddingService, IReadOnlyList<string> chunks, CancellationToken ct)
    {
        float[]? sum = null;
        foreach (var chunk in chunks)
        {
            var emb = await embeddingService.EmbedAsync(chunk, ct);
            if (sum is null)
            {
                sum = new float[emb.Length];
            }
            for (int i = 0; i < emb.Length; i++)
            {
                sum[i] += emb[i];
            }
        }

        if (sum is null)
        {
            throw new InvalidOperationException("No embeddings produced.");
        }

        var count = chunks.Count;
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] /= count;
        }
        return sum;
    }
}
