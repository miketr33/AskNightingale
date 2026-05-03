using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AskNightingale.Services.Embeddings;

public class VoyageEmbeddingProvider : IEmbeddingProvider
{
    private const string ApiUrl = "https://api.voyageai.com/v1/embeddings";

    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;

    public VoyageEmbeddingProvider(HttpClient http, IConfiguration config)
    {
        _http = http;
        _apiKey = config["VOYAGE_API_KEY"]
            ?? throw new InvalidOperationException(
                "VOYAGE_API_KEY is not configured. Set it in .env, user secrets, or environment.");
        _model = config["VOYAGE_MODEL"] ?? "voyage-3";
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        EmbeddingPurpose purpose,
        CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var body = new VoyageRequest(
            Input: texts.ToArray(),
            Model: _model,
            InputType: purpose == EmbeddingPurpose.Query ? "query" : "document"
        );

        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var error = await resp.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Voyage API returned {(int)resp.StatusCode}: {error}");
        }

        var parsed = await resp.Content.ReadFromJsonAsync<VoyageResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty response from Voyage API.");

        // Defensive ordering: the API returns "index" alongside each embedding;
        // sort by it so the result aligns with the input order regardless of
        // any future API behaviour change.
        return parsed.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    private record VoyageRequest(
        [property: JsonPropertyName("input")] string[] Input,
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input_type")] string InputType
    );

    private record VoyageResponse(
        [property: JsonPropertyName("data")] VoyageEmbedding[] Data,
        [property: JsonPropertyName("model")] string Model
    );

    private record VoyageEmbedding(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[] Embedding
    );
}
