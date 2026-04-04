using GeniusAgent.Application;
using GeniusAgent.Core.Interfaces;
using GeniusAgent.Infrastructure.Services;
using OpenAI;
using Scalar.AspNetCore;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Shared OpenAI client (Ollama-compatible)
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return new OpenAIClient(new ApiKeyCredential("ollama"), new OpenAIClientOptions
    {
        Endpoint = new Uri(config["OpenAi:Endpoint"] ?? "http://localhost:11434/v1")
    });
});

// DI Container Registration
builder.Services.AddScoped<FlowOrchestrator>();
builder.Services.AddSingleton<ILLMService, OpenAiService>();
builder.Services.AddSingleton<IKnowledgeSource, VectorDbAnalyzer>();
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