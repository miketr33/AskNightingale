using AskNightingale.Components;
using AskNightingale.Services;
using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Rag;
using DotNetEnv;

// Walk up from cwd to find .env. Silent no-op in production where no .env exists.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// LLM stack. PR #3-stretch (tomorrow) adds BedrockLlmProvider beside this
// one and swaps the registration via config.
builder.Services.AddHttpClient<ILlmProvider, AnthropicLlmProvider>();

// Embedding + RAG stack. The vector store is registered as a Singleton
// twice so both InMemoryVectorStore (needed by RagBootstrapper for
// persistence) and IVectorStore (needed by LlmChatService) resolve to
// the SAME instance.
builder.Services.AddHttpClient<IEmbeddingProvider, VoyageEmbeddingProvider>();
builder.Services.AddSingleton<Chunker>();
builder.Services.AddSingleton<InMemoryVectorStore>();
builder.Services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<InMemoryVectorStore>());
builder.Services.AddTransient<RagBootstrapper>();

// Guardrails. Each layer is its own class so the architecture maps 1:1
// to code: PR #8 retrieval threshold, PR #9 input filter, PR #10 output judge.
builder.Services.AddSingleton<RetrievalGuard>();
builder.Services.AddSingleton<InputGuard>();

// Chat service: retrieval-augmented (PR #4d-ii) with retrieval-threshold
// short-circuit (PR #8).
builder.Services.AddScoped<IChatService, LlmChatService>();

var app = builder.Build();

// Bootstrap the RAG corpus before serving requests. EnsureLoadedAsync is
// idempotent — on subsequent restarts it loads data/embeddings.json
// instead of re-embedding the corpus.
using (var bootstrapScope = app.Services.CreateScope())
{
    var bootstrapper = bootstrapScope.ServiceProvider.GetRequiredService<RagBootstrapper>();
    await bootstrapper.EnsureLoadedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
