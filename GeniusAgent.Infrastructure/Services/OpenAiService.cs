using GeniusAgent.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace GeniusAgent.Infrastructure.Services;

public class OpenAiService : ILLMService
{
    private readonly ChatClient _chatClient;

    public OpenAiService(IConfiguration config)
    {
        var model = config["OpenAi:Model"] ?? "codellama";

        // Point the client to your local Ollama endpoint
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri(config["OpenAi:Endpoint"] ?? "http://localhost:11434/v1")
        };

        // Ollama doesn't validate the key, but the SDK needs a placeholder
        var client = new OpenAIClient(new ApiKeyCredential("ollama"), options);
        _chatClient = client.GetChatClient(model);
    }

    public async Task<string> GenerateArtifactsAsync(string prompt, string context)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(GetMasterSystemPrompt()),
            new UserChatMessage($"Existing Patterns:\n{context}"),
            new UserChatMessage($"Requirement: {prompt}")
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0.2f // Low temperature for consistent code structure
        };

        var response = await _chatClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text;
    }

    public async Task<string> RefineCodeAsync(string originalCode, string errorLog)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage("You are a .NET debugging assistant. Fix the provided code based on the compiler errors."),
            new UserChatMessage($"Original Code:\n{originalCode}"),
            new UserChatMessage($"Compiler/Test Errors:\n{errorLog}"),
            new UserChatMessage("Please provide the corrected code using the same '// File: Path' format.")
        };

        var response = await _chatClient.CompleteChatAsync(messages);
        return response.Value.Content[0].Text;
    }

    public async IAsyncEnumerable<string> GenerateArtifactsStreamAsync(string prompt, string context)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(GetMasterSystemPrompt()),
            new UserChatMessage($"Context:\n{context}"),
            new UserChatMessage($"Task: {prompt}")
        };

        await foreach (var update in _chatClient.CompleteChatStreamingAsync(messages))
        {
            foreach (var part in update.ContentUpdate)
            {
                if (!string.IsNullOrEmpty(part.Text))
                {
                    yield return part.Text;
                }
            }
        }
    }

    private string GetMasterSystemPrompt()
    {
        return @"### ROLE: Senior .NET Developer
### TASK: Generate code artifacts based on the provided patterns.
### RULES:
1. ONLY output code. No conversational filler.
2. Every file MUST start with the header: // File: [RelativePath/FileName.cs]
3. Use file-scoped namespaces.
4. If a build error is provided, fix ONLY that specific error.";
    }
}