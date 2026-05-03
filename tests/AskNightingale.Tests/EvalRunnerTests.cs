using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AskNightingale.Services;
using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Rag;
using DotNetEnv;
using Microsoft.Extensions.Configuration;

namespace AskNightingale.Tests;

// Eval runner. Walks evals/cases.json against the live RAG pipeline,
// scores each case with simple text-contains heuristics, and writes a
// markdown report to evals/results-pre-guardrails.md.
//
// Gated behind the RUN_EVAL environment variable so it doesn't fire on
// every dotnet test (would cost API credits). Run manually:
//
//     RUN_EVAL=1 dotnet test --filter "FullyQualifiedName~EvalRunner"      (Bash / Git Bash)
//     $env:RUN_EVAL=1; dotnet test --filter "FullyQualifiedName~EvalRunner" (PowerShell)
//
// Requires ANTHROPIC_API_KEY and VOYAGE_API_KEY (loaded automatically
// from .env via DotNetEnv if present).
public class EvalRunnerTests
{
    [Fact]
    [Trait("Category", "Eval")]
    public async Task Capture_eval_baseline()
    {
        if (Environment.GetEnvironmentVariable("RUN_EVAL") is null)
        {
            // Skip silently unless explicitly requested. Keeps `dotnet test`
            // free for the non-eval test suite.
            return;
        }

        // Resolve repo root first so .env loading is independent of however
        // dotnet test set the cwd (which is often the test bin directory,
        // not the repo root — Env.TraversePath() doesn't reliably reach it).
        var repoRoot = FindRepoRoot();
        var envPath = Path.Combine(repoRoot, ".env");
        if (File.Exists(envPath))
        {
            Env.Load(envPath);
        }

        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        var voyageKey = Environment.GetEnvironmentVariable("VOYAGE_API_KEY");
        Assert.False(string.IsNullOrWhiteSpace(anthropicKey),
            $"ANTHROPIC_API_KEY required (looked in {envPath} and shell env)");
        Assert.False(string.IsNullOrWhiteSpace(voyageKey),
            $"VOYAGE_API_KEY required (looked in {envPath} and shell env)");

        var pipeline = await BuildLivePipelineAsync(repoRoot);
        var cases = LoadCases(Path.Combine(repoRoot, "evals", "cases.json"));

        var results = new List<EvalRunResult>();
        foreach (var c in cases)
        {
            try
            {
                var response = await pipeline.RespondAsync(c.Question);
                var (passed, reason) = Evaluate(c, response.Content);
                results.Add(new EvalRunResult(c, response.Content, passed, reason));
            }
            catch (Exception ex)
            {
                results.Add(new EvalRunResult(c, "", false, $"Threw: {ex.Message}"));
            }
        }

        var outputFile = Environment.GetEnvironmentVariable("EVAL_OUTPUT_FILE")
            ?? "results-pre-guardrails.md";
        var outputPath = Path.Combine(repoRoot, "evals", outputFile);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await File.WriteAllTextAsync(outputPath, BuildReport(results));
    }

    private static async Task<IChatService> BuildLivePipelineAsync(string repoRoot)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ANTHROPIC_API_KEY"] = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
                ["VOYAGE_API_KEY"] = Environment.GetEnvironmentVariable("VOYAGE_API_KEY"),
                ["ANTHROPIC_MODEL"] =
                    Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? "claude-haiku-4-5",
                ["RAG_CORPUS_PATH"] =
                    Path.Combine(repoRoot, "src", "AskNightingale", "data", "notes-on-nursing.txt"),
                ["RAG_STORE_PATH"] =
                    Path.Combine(repoRoot, "src", "AskNightingale", "data", "embeddings.json")
            })
            .Build();

        // Single shared HttpClient for the eval run — fine for one-shot.
        var http = new HttpClient();
        var llm = new AnthropicLlmProvider(http, config);
        var embedder = new VoyageEmbeddingProvider(http, config);
        var chunker = new Chunker();
        var store = new InMemoryVectorStore();
        var bootstrapper = new RagBootstrapper(chunker, embedder, store, config);
        await bootstrapper.EnsureLoadedAsync();
        var retrievalGuard = new RetrievalGuard(config);

        return new LlmChatService(llm, embedder, store, retrievalGuard);
    }

    private static IReadOnlyList<EvalCase> LoadCases(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<EvalCase[]>(json)
            ?? throw new InvalidOperationException("Failed to parse eval cases.");
    }

    private static (bool Passed, string Reason) Evaluate(EvalCase c, string answer)
    {
        var lower = answer.ToLowerInvariant();
        var anyOf = c.MustContainAnyOf.Select(t => t.ToLowerInvariant()).ToArray();
        var none = (c.MustNotContain ?? []).Select(t => t.ToLowerInvariant()).ToArray();

        var matchedAnyOf = anyOf.Length == 0 || anyOf.Any(t => lower.Contains(t));
        var hitForbidden = none.FirstOrDefault(t => lower.Contains(t));

        if (!matchedAnyOf)
            return (false, $"answer did not contain any of [{string.Join(", ", c.MustContainAnyOf)}]");
        if (hitForbidden is not null)
            return (false, $"answer contained forbidden phrase '{hitForbidden}'");
        return (true, "ok");
    }

    private static string BuildReport(IReadOnlyList<EvalRunResult> results)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Eval results");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Category | Pass | Total | Rate |");
        sb.AppendLine("|---|---|---|---|");

        var byCategory = results.GroupBy(r => r.Case.Category).OrderBy(g => g.Key).ToList();
        foreach (var grp in byCategory)
        {
            var pass = grp.Count(r => r.Passed);
            var total = grp.Count();
            sb.AppendLine($"| {grp.Key} | {pass} | {total} | {pass * 100 / total}% |");
        }
        var totalPass = results.Count(r => r.Passed);
        sb.AppendLine(
            $"| **TOTAL** | **{totalPass}** | **{results.Count}** | **{totalPass * 100 / results.Count}%** |");
        sb.AppendLine();

        sb.AppendLine("## Cases");
        sb.AppendLine();
        foreach (var grp in byCategory)
        {
            sb.AppendLine($"### {grp.Key}");
            sb.AppendLine();
            foreach (var r in grp)
            {
                var status = r.Passed ? "PASS" : "FAIL";
                sb.AppendLine($"#### `{r.Case.Id}` — {status}");
                sb.AppendLine();
                sb.AppendLine($"**Question:** {r.Case.Question}");
                sb.AppendLine();
                if (!r.Passed) sb.AppendLine($"**Reason:** {r.Reason}");
                sb.AppendLine();
                sb.AppendLine("**Answer:**");
                sb.AppendLine();
                var truncated = r.Answer.Length > 500 ? r.Answer[..500] + "..." : r.Answer;
                sb.AppendLine("> " + truncated.Replace("\n", "\n> "));
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AskNightingale.slnx")))
            dir = dir.Parent;
        return dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not find AskNightingale.slnx walking up from " + AppContext.BaseDirectory);
    }

    public record EvalCase(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("category")] string Category,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("expected")] string Expected,
        [property: JsonPropertyName("must_contain_any_of")] string[] MustContainAnyOf,
        [property: JsonPropertyName("must_not_contain")] string[]? MustNotContain = null);

    private record EvalRunResult(EvalCase Case, string Answer, bool Passed, string Reason);
}
