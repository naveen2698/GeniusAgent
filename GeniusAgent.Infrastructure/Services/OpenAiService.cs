using GeniusAgent.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenAI.Chat;

namespace GeniusAgent.Infrastructure.Services;

public class OpenAiService : ILLMService
{
    private readonly ChatClient _chatClient;

    public OpenAiService(OpenAIClient client, IConfiguration config)
    {
        _chatClient = client.GetChatClient(config["OpenAi:Model"] ?? "codellama");
    }

    public async Task<string> GenerateArtifactsAsync(string prompt, string context)
    {
        var messages = new ChatMessage[]
        {
            new SystemChatMessage(GetMasterSystemPrompt()),
            new UserChatMessage($"Existing Patterns:\n{context}"),
            new UserChatMessage($"Requirement: {prompt}")
        };

        return await GetCompletionAsync(messages, new ChatCompletionOptions
        {
            Temperature = 0.2f
        });
    }

    public async Task<string> AlignTerminologyAsync(string goal, string context)
    {
        var systemPrompt = @"You are an Enterprise Semantic Normalizer. 
        Your task is to:
        1. Analyze the user goal and the retrieved system knowledge (code, docs, catalogs).
        2. Identify inconsistent terminology (e.g., 'Client' vs 'Customer', 'ID' vs 'UUID').
        3. Normalize all terms to the authoritative enterprise standards found in the context.
        4. Resolve contradictions between old documentation and current code.
        5. Output a 'Normalized Requirement Specification' that is structured and reviewable.";

        var messages = new ChatMessage[]
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage($"Authoritative Context:\n{context}"),
            new UserChatMessage($"User Goal to Normalize: {goal}")
        };

        return await GetCompletionAsync(messages, new ChatCompletionOptions
        {
            Temperature = 0.1f
        });
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

        return await GetCompletionAsync(messages);
    }

    private async Task<string> GetCompletionAsync(ChatMessage[] messages, ChatCompletionOptions? options = null)
    {
        var response = await _chatClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text;
    }

    private static string GetMasterSystemPrompt()
    {
        return @"Act as an Enterprise Architect. For every request:
        1. Generate the .NET code following Clean Architecture.
        2. Every file MUST include a header: // Source: [Document Name], Rule: [Rule ID]. If content is inferred, mark as // AI-INFERRED: [Reason].
        3. Use file-scoped namespaces.
        4. If a build error is provided, fix ONLY that specific error.
        5. Link every class to a Requirement ID found in the context.
        6. You MUST also generate a technical design document (e.g. Design.md) describing the architecture.
        Output format: // File: [Path].";
    }
}