using AskNightingale.Components;
using AskNightingale.Services;
using AskNightingale.Services.Llm;
using DotNetEnv;

// Walk up from cwd to find .env. Silent no-op in production where no .env exists.
Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// LLM stack. PR #3-stretch (tomorrow) adds BedrockLlmProvider beside this
// one and swaps the registration via config.
builder.Services.AddHttpClient<ILlmProvider, AnthropicLlmProvider>();
builder.Services.AddScoped<IChatService, LlmChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
