using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace GeniusAgent.Application;

public class FlowOrchestrator(
    ILLMService llm,
    IKnowledgeSource knowledge,
    ICodeSandbox sandbox,
    IVersionControl vcs)
{
    public async Task ExecuteWorkflowAsync(string userGoal)
    {
        // STAGE 1: EXTRACTION (Deterministic + Retrieval)
        var context = await ExtractContextAsync(userGoal);

        // STAGE 2: NORMALIZATION (Semantic Alignment)
        var alignedRequirements = await NormalizeRequirementsAsync(userGoal, context);

        // STAGE 3: COMPOSITION (Generative)
        var artifacts = await ComposeArtifactsAsync(alignedRequirements);

        // STAGE 4: VALIDATION (Deterministic Governance)
        var validation = await ValidateComplianceAsync(artifacts);

        if (!validation.IsSuccess)
        {
            // High-Confidence Self-Healing
            artifacts = await RefineArtifactsAsync(artifacts, validation.Errors);

            // Final deterministic check after refinement
            validation = await ValidateComplianceAsync(artifacts);
            if (!validation.IsSuccess)
            {
                Console.WriteLine("--- Governance Gate: Refined code failed compliance checks. ---");
                return;
            }
        }

        // STAGE 6: DELIVERY
        await vcs.CreatePullRequestAsync(GenerateBranchName(userGoal), artifacts);
    }

    private Task<string> ExtractContextAsync(string goal) =>
        knowledge.GetRelevantPatternsAsync(goal);

    private Task<string> NormalizeRequirementsAsync(string goal, string context) =>
        llm.AlignTerminologyAsync(goal, context);

    private async Task<List<CodeArtifact>> ComposeArtifactsAsync(string requirements)
    {
        // The requirement is to produce 'structured, reviewable outputs'
        // such as requirements documentation and technical design specifications.

        // 1. Generative Stage
        // We call the LLM to generate the raw Markdown containing code and design docs.
        var rawResponse = await llm.GenerateArtifactsAsync(requirements, string.Empty);

        // 2. Deterministic Processing
        // We use the static ParseArtifacts to turn raw text into typed objects.
        // This maintains a clear system boundary between AI analysis and deterministic processing.
        var artifacts = ParseArtifacts(rawResponse);

        // 3. Post-Processing for Traceability
        // We ensure each artifact is tagged for human review.
        foreach (var artifact in artifacts)
        {
            Console.WriteLine($"[Composition] Generated {artifact.Type}: {artifact.FileName}");
        }

        return artifacts;
    }

    private async Task<List<CodeArtifact>> RefineArtifactsAsync(List<CodeArtifact> originalArtifacts, List<string> errors)
    {
        // 1. Prepare the Technical Context for the LLM
        // We concatenate the errors into a structured log for the AI to analyze.
        string errorContext = string.Join(Environment.NewLine, errors);

        // We convert the failed artifacts back into a readable format for the LLM.
        StringBuilder originalCodeBuilder = new();
        foreach (var artifact in originalArtifacts)
        {
            originalCodeBuilder.AppendLine($"// File: {artifact.FullPath}");
            originalCodeBuilder.AppendLine(artifact.Content);
        }

        Console.WriteLine($"\n--- Technical Errors Detected. Initiating Self-Healing Stage ---");

        // 2. Execute the Refinement Call
        // This call uses the specialized RefineCodeAsync interface to fix the logic.
        var fixedRawResponse = await llm.RefineCodeAsync(originalCodeBuilder.ToString(), errorContext);

        // 3. Parse the Corrected Output
        // We reuse the deterministic ParseArtifacts logic to ensure the new files are valid.
        var refinedArtifacts = ParseArtifacts(fixedRawResponse);

        return refinedArtifacts;
    }

    private static string GenerateBranchName(string description)
    {
        // 1. Normalize the description (remove special characters and spaces)
        // This ensures system boundaries and predictable branch naming
        string slug = Regex.Replace(description.ToLower(), @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');

        // 2. Truncate for safety and append a short timestamp for uniqueness within a day
        // This supports the requirement for repeatable generation with predictable outcomes.
        string shortSlug = slug.Length > 30 ? slug[..30] : slug;
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");

        return $"ai/feat-{shortSlug}-{timestamp}";
    }

    private async Task<ValidationResult> ValidateComplianceAsync(IEnumerable<CodeArtifact> artifacts)
    {
        // 1. Technical Validation (Existing Build Check)
        var buildResult = await sandbox.RunBuildAndTestAsync(artifacts);
        if (!buildResult.IsSuccess) return buildResult;

        // 2. Enterprise Governance Validation
        var complianceErrors = new List<string>();

        bool hasDesignDoc = artifacts.Any(a => a.Type == ArtifactType.Service || a.FileName.EndsWith(".md"));
        if (!hasDesignDoc)
            complianceErrors.Add("Compliance Error: No corresponding design or traceability documentation found.");

        foreach (var artifact in artifacts)
        {
            if (artifact.Type == ArtifactType.Service && !artifact.Content.Contains("// Source:"))
                complianceErrors.Add($"Traceability Error: {artifact.FileName} is missing authoritative source citations.");
        }

        return complianceErrors.Count > 0
            ? new ValidationResult(false, complianceErrors)
            : new ValidationResult(true, new List<string>());
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

