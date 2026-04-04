using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace GeniusAgent.Application;

public class FlowOrchestrator(ILLMService llm, IRepositoryAnalyzer analyzer, ICodeSandbox sandbox, IVersionControl vcs)
{
    private const int MaxRetries = 3;

    public async Task ExecuteWorkflowAsync(string description)
    {
        var patterns = await analyzer.GetRelevantPatternsAsync(description);

        StringBuilder fullResponse = new();

        Console.WriteLine("--- AI is generating code ---");

        await foreach (var chunk in llm.GenerateArtifactsStreamAsync(description, patterns))
        {
            Console.Write(chunk); // Streams code to terminal in real-time
            fullResponse.Append(chunk);
        }

        var rawCode = fullResponse.ToString();
        var artifacts = ParseArtifacts(rawCode);

        var result = await sandbox.RunBuildAndTestAsync(artifacts);
        for (int attempt = 0; attempt < MaxRetries && !result.IsSuccess; attempt++)
        {
            Console.WriteLine($"\n--- Build failed. Refining code (attempt {attempt + 1}/{MaxRetries}) ---");
            rawCode = await llm.RefineCodeAsync(rawCode, string.Join("\n", result.Errors));
            artifacts = ParseArtifacts(rawCode);
            result = await sandbox.RunBuildAndTestAsync(artifacts);
        }

        if (!result.IsSuccess)
        {
            Console.WriteLine("--- Code generation failed after all retry attempts. PR will not be created. ---");
            return;
        }

        await vcs.CreatePullRequestAsync($"ai/feat-{Guid.NewGuid().ToString()[..4]}", artifacts);
    }

    private async Task<ValidationResult> ValidateEnterpriseCompliance(IEnumerable<CodeArtifact> artifacts)
    {
        var buildResult = await sandbox.RunBuildAndTestAsync(artifacts);
        if (!buildResult.IsSuccess) return buildResult;

        // Check for mandatory documentation
        bool hasDesignDoc = artifacts.Any(a => a.Type == ArtifactType.SystemDesign);
        if (!hasDesignDoc)
        {
            return new ValidationResult(false, new List<string> { "Compliance Error: No System Design/ADR artifact generated for this change." });
        }

        return new ValidationResult(true, new List<string>());
    }

    private static List<CodeArtifact> ParseArtifacts(string input)
    {
        var artifacts = new List<CodeArtifact>();
        var regex = new Regex(@"// File: (?<path>[\w\.\/-]+)\r?\n(?<code>[\s\S]*?)(?=// File:|$|```)", RegexOptions.Multiline);
        foreach (Match match in regex.Matches(input))
        {
            var path = match.Groups["path"].Value.Trim();
            artifacts.Add(new CodeArtifact
            {
                FileName = Path.GetFileName(path),
                RelativePath = Path.GetDirectoryName(path) ?? "",
                Content = match.Groups["code"].Value.Trim(),
                Type = path.Contains("Service") ? ArtifactType.Service : ArtifactType.DomainModel
            });
        }
        return artifacts;
    }
}