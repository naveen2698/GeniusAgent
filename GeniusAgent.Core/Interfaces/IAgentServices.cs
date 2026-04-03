using GeniusAgent.Core.Models;

namespace GeniusAgent.Core.Interfaces;

public interface ILLMService
{
    Task<string> GenerateArtifactsAsync(string prompt, string context);
    IAsyncEnumerable<string> GenerateArtifactsStreamAsync(string prompt, string context); // New
    Task<string> RefineCodeAsync(string originalCode, string errorLog);
}

public interface IRepositoryAnalyzer
{
    Task<string> GetRelevantPatternsAsync(string userIntent);
}

public interface ICodeSandbox
{
    Task<ValidationResult> RunBuildAndTestAsync(IEnumerable<CodeArtifact> artifacts);
}

public interface IVersionControl
{
    Task CreatePullRequestAsync(string branchName, IEnumerable<CodeArtifact> artifacts);
}