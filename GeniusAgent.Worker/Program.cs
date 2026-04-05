using GeniusAgent.Application;
using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;
using GeniusAgent.Infrastructure.Services;
using OpenAI;
using Scalar.AspNetCore;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Compliance options
var complianceOptions = new ComplianceOptions();
builder.Configuration.GetSection("Compliance").Bind(complianceOptions);
builder.Services.AddSingleton(complianceOptions);

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
    try
    {
        var result = await orchestrator.ExecuteWorkflowAsync(prompt);
        return result.IsSuccess
            ? Results.Ok(new { message = "Code generation completed. PR created." })
            : Results.UnprocessableEntity(new { message = "Workflow failed compliance checks.", errors = result.Errors });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
})
.WithName("GenerateCode")
.WithDescription("Starts the AI code-generation workflow for the given prompt.");

app.Run();