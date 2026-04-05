using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;

namespace GeniusAgent.Infrastructure.Services;

public class GitHubService : IVersionControl
{
    public Task CreatePullRequestAsync(string branchName, IEnumerable<CodeArtifact> artifacts)
    {
        // TODO: Implement Octokit branch/commit/PR using GitHub App Installation Token.
        return Task.CompletedTask;
    }
}