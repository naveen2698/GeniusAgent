using GeniusAgent.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace GeniusAgent.Infrastructure.Services;

public class OpenAiService : ILLMService
{
    private static readonly string MasterSystemPrompt = string.Join("\n",
        "You generate .NET 10 C# code. Output ONLY files in this exact format.",
        "Do NOT add any explanation or text outside the file markers.",
        "IMPORTANT: Implement EVERY method completely. NEVER use placeholder comments like 'Similar methods...' or '...'.",
        "",
        "EXAMPLE OUTPUT:",
        "",
        "// File: Design.md",
        "# Design",
        "(short description of the solution)",
        "",
        "// File: CalculatorService.cs",
        "namespace MyApp;",
        "",
        "/// <summary>Provides basic arithmetic operations.</summary>",
        "public class CalculatorService",
        "{",
        "    /// <summary>Adds two numbers.</summary>",
        "    public double Add(double a, double b) => a + b;",
        "    /// <summary>Subtracts b from a.</summary>",
        "    public double Subtract(double a, double b) => a - b;",
        "    /// <summary>Multiplies two numbers.</summary>",
        "    public double Multiply(double a, double b) => a * b;",
        "    /// <summary>Divides a by b.</summary>",
        "    public double Divide(double a, double b)",
        "    {",
        "        if (b == 0) throw new DivideByZeroException();",
        "        return a / b;",
        "    }",
        "}",
        "",
        "// File: CalculatorServiceTest.cs",
        "using Xunit;",
        "namespace MyApp.Tests;",
        "",
        "public class CalculatorServiceTest",
        "{",
        "    private readonly CalculatorService _svc = new();",
        "    [Fact] public void Add_ReturnsSum() => Assert.Equal(5.0, _svc.Add(2, 3));",
        "    [Fact] public void Subtract_ReturnsDiff() => Assert.Equal(1.0, _svc.Subtract(3, 2));",
        "    [Fact] public void Multiply_ReturnsProduct() => Assert.Equal(6.0, _svc.Multiply(2, 3));",
        "    [Fact] public void Divide_ReturnsQuotient() => Assert.Equal(2.0, _svc.Divide(6, 3));",
        "    [Fact] public void Divide_ByZero_Throws() => Assert.Throws<DivideByZeroException>(() => _svc.Divide(1, 0));",
        "}",
        "",
        "RULES:",
        "- Start each file with: // File: <filename.cs> or // File: <filename.md>",
        "- Add XML doc comments (/// <summary>) on every public class and method.",
        "- Always include a Design.md file.",
        "- Implement ALL methods completely. No stubs. No placeholders.",
        "- Service filenames must contain the word Service.",
        "- Test filenames must end with Test.cs and use xUnit. Test every public method.",
        "- Output ONLY the file content. No explanation before or after the code.");

    private static readonly string RefineSystemPrompt = string.Join(" ",
        "You fix .NET 10 C# code.",
        "Output ONLY corrected files using '// File: <name>' format.",
        "Add XML doc comments (/// <summary>) on every public class and method.",
        "Implement ALL methods completely. No stubs. No placeholders.",
        "Include a Design.md.",
        "No explanation. No chat. Only output corrected files.");

    private static readonly string NormalizeSystemPrompt =
        "You normalize software requirements. Output ONLY a short, structured list of requirements. No explanation.";

    private readonly ChatClient _chatClient;

    public OpenAiService(OpenAIClient client, IConfiguration config)
    {
        _chatClient = client.GetChatClient(config["OpenAi:Model"] ?? "codellama");
    }

    public Task<string> GenerateArtifactsAsync(string prompt, string context) =>
        GetCompletionAsync(
            MasterSystemPrompt,
            FormatUserMessage(context, $"Generate .NET code for: {prompt}"),
            new ChatCompletionOptions { Temperature = 0.2f });

    public Task<string> AlignTerminologyAsync(string goal, string context) =>
        GetCompletionAsync(
            NormalizeSystemPrompt,
            FormatUserMessage(context, $"Normalize this requirement into a structured list:\n{goal}"),
            new ChatCompletionOptions { Temperature = 0.1f });

    public Task<string> RefineCodeAsync(string originalCode, string errorLog) =>
        GetCompletionAsync(
            RefineSystemPrompt,
            $"Code:\n{originalCode}\n\nErrors:\n{errorLog}\n\nOutput the corrected files:");

    private static string FormatUserMessage(string context, string instruction) =>
        string.IsNullOrWhiteSpace(context) ? instruction : $"Context:\n{context}\n\n{instruction}";

    private async Task<string> GetCompletionAsync(string systemPrompt, string userMessage, ChatCompletionOptions? options = null)
    {
        ChatMessage[] messages = [new SystemChatMessage(systemPrompt), new UserChatMessage(userMessage)];
        var response = await _chatClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text;
    }
}
