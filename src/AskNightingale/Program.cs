using AskNightingale.Components;
using AskNightingale.Services;
using AskNightingale.Services.Embeddings;
using AskNightingale.Services.Guardrails;
using AskNightingale.Services.Llm;
using AskNightingale.Services.Rag;
using DotNetEnv;

// Load .env from the repo if present; silent no-op in production.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<ILlmProvider, AnthropicLlmProvider>();
builder.Services.AddHttpClient<IEmbeddingProvider, VoyageEmbeddingProvider>();

builder.Services.AddSingleton<Chunker>();
// Vector store registered twice: bootstrapper resolves the concrete type for
// persistence; chat service resolves the interface for retrieval. Same instance.
builder.Services.AddSingleton<InMemoryVectorStore>();
builder.Services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<InMemoryVectorStore>());
builder.Services.AddTransient<RagBootstrapper>();

builder.Services.AddSingleton<RetrievalGuard>();
builder.Services.AddSingleton<InputGuard>();
builder.Services.AddSingleton<OutputJudge>();

builder.Services.AddScoped<IChatService, LlmChatService>();

var app = builder.Build();

// Bootstrap the RAG corpus before serving requests; idempotent on restart.
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
