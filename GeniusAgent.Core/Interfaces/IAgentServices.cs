using GeniusAgent.Core.Models;

namespace GeniusAgent.Core.Interfaces;

public interface ILLMService
{
    Task<string> GenerateArtifactsAsync(string prompt, string context);
    Task<string> RefineCodeAsync(string originalCode, string errorLog);
    Task<string> AlignTerminologyAsync(string goal, string context);
}

public interface IKnowledgeSource
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