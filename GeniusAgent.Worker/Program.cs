using GeniusAgent.Application;
using GeniusAgent.Core.Interfaces;
using GeniusAgent.Infrastructure.Services;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// DI Container Registration
builder.Services.AddScoped<FlowOrchestrator>();
builder.Services.AddSingleton<ILLMService, OpenAiService>();
builder.Services.AddSingleton<IRepositoryAnalyzer, VectorDbAnalyzer>();
builder.Services.AddSingleton<ICodeSandbox, DotNetCliValidator>();
builder.Services.AddSingleton<IVersionControl, GitHubService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapPost("/generate", async (string prompt, FlowOrchestrator orchestrator) =>
{
    await orchestrator.ExecuteWorkflowAsync(prompt);
    return Results.Ok("Agent started processing...");
})
.WithName("GenerateCode")
.WithDescription("Starts the AI code-generation workflow for the given prompt.");

app.Run();