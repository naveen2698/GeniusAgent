using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace GeniusAgent.Application;

public class FlowOrchestrator(
    ILLMService llm,
    IKnowledgeSource knowledge,
    ICodeSandbox sandbox,
    IVersionControl vcs,
    ComplianceOptions complianceOptions)
{
    private static readonly string OutputDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), ".."));
    private static readonly string SandboxDir = Path.Combine(OutputDir, "SandboxProject");
    public async Task<ValidationResult> ExecuteWorkflowAsync(string userGoal)
    {
        // STAGE 1: EXTRACTION (Deterministic + Retrieval)
        var context = await ExtractContextAsync(userGoal);

        // STAGE 2: NORMALIZATION (Semantic Alignment)
        var alignedRequirements = await NormalizeRequirementsAsync(userGoal, context);

        // STAGE 3: COMPOSITION (Generative)
        var artifacts = await ComposeArtifactsAsync(userGoal, alignedRequirements);

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
                return validation;
            }
        }

        // STAGE 5: WRITE ARTIFACTS TO SANDBOX
        await WriteArtifactsToSandboxAsync(artifacts);

        // STAGE 6: DELIVERY
        await vcs.CreatePullRequestAsync(GenerateBranchName(userGoal), artifacts);
        return new ValidationResult(true, []);
    }

    private Task<string> ExtractContextAsync(string goal) =>
        knowledge.GetRelevantPatternsAsync(goal);

    private Task<string> NormalizeRequirementsAsync(string goal, string context) =>
        llm.AlignTerminologyAsync(goal, context);

    private async Task<List<CodeArtifact>> ComposeArtifactsAsync(string userGoal, string alignedContext)
    {
        var rawResponse = await llm.GenerateArtifactsAsync(userGoal, alignedContext);
        await WriteDiagnosticFileAsync(OutputDir, "RawOutput.txt", rawResponse);

        var artifacts = ParseArtifacts(rawResponse);

        Console.WriteLine($"[Composition] Parsed {artifacts.Count} artifact(s) from LLM output.");
        foreach (var artifact in artifacts)
            Console.WriteLine($"[Composition]   {artifact.Type}: {artifact.FileName}");

        return artifacts;
    }

    private async Task<List<CodeArtifact>> RefineArtifactsAsync(List<CodeArtifact> originalArtifacts, List<string> errors)
    {
        string errorContext = string.Join(Environment.NewLine, errors);

        StringBuilder originalCodeBuilder = new();
        foreach (var artifact in originalArtifacts)
        {
            originalCodeBuilder.AppendLine($"// File: {artifact.FullPath}");
            originalCodeBuilder.AppendLine(artifact.Content);
        }

        Console.WriteLine($"\n--- Self-Healing: {errors.Count} error(s) detected ---");

        var fixedRawResponse = await llm.RefineCodeAsync(originalCodeBuilder.ToString(), errorContext);
        await WriteDiagnosticFileAsync(SandboxDir, "RawOutput_Refined.txt", fixedRawResponse);

        return ParseArtifacts(fixedRawResponse);
    }

    private static string GenerateBranchName(string description)
    {
        string slug = Regex.Replace(description.ToLower(), @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-").Trim('-');
        string shortSlug = slug.Length > 30 ? slug[..30] : slug;
        return $"ai/feat-{shortSlug}-{DateTime.UtcNow:yyyyMMdd-HHmm}";
    }

    private async Task<ValidationResult> ValidateComplianceAsync(IEnumerable<CodeArtifact> artifacts)
    {
        if (!complianceOptions.SkipSandboxValidation)
        {
            var buildResult = await sandbox.RunBuildAndTestAsync(artifacts);
            if (!buildResult.IsSuccess) return buildResult;
        }

        var complianceErrors = new List<string>();

        if (complianceOptions.EnforceDesignDocs)
        {
            bool hasDesignDoc = artifacts.Any(a => a.Type == ArtifactType.SystemDesign);
            if (!hasDesignDoc)
                complianceErrors.Add("Compliance Error: No design documentation found.");
        }

        if (complianceOptions.EnforceSourceCitations)
        {
            foreach (var artifact in artifacts.Where(a => a.Type == ArtifactType.Service && !a.Content.Contains("// Source:")))
                complianceErrors.Add($"Traceability Error: {artifact.FileName} is missing source citations.");
        }

        // Reject placeholder / stub code from lazy LLM output
        string[] placeholderPatterns = ["Similar methods", "similar pattern", "left out", "follow a similar", "// ...", "// TODO"];
        foreach (var artifact in artifacts.Where(a => a.FileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var pattern in placeholderPatterns)
            {
                if (artifact.Content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    complianceErrors.Add($"Completeness Error: {artifact.FileName} contains placeholder '{pattern}'.");
                    break;
                }
            }
        }

        return complianceErrors.Count > 0
            ? new ValidationResult(false, complianceErrors)
            : new ValidationResult(true, []);
    }

    private static List<CodeArtifact> ParseArtifacts(string input)
    {
        var artifacts = new List<CodeArtifact>();

        // Primary format: // File: <path>
        var sections = Regex.Split(input, @"(?=^//\s*File:\s*)", RegexOptions.Multiline);

        foreach (var section in sections)
        {
            var headerMatch = Regex.Match(section, @"^//\s*File:\s*(?<path>\S+)", RegexOptions.Multiline);
            if (!headerMatch.Success) continue;

            var path = headerMatch.Groups["path"].Value.Trim();

            // Only accept .cs and .md files — skip .java or any other extensions
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                continue;

            var code = section[headerMatch.Length..].Trim();

            // Strip code-fence markers (```csharp, ```markdown, ```, etc.)
            code = Regex.Replace(code, @"```\w*\r?\n?", "").Trim();

            if (string.IsNullOrWhiteSpace(code)) continue;

            artifacts.Add(new CodeArtifact
            {
                FileName = Path.GetFileName(path),
                RelativePath = Path.GetDirectoryName(path) ?? "",
                Content = CleanContent(code, path),
                Type = ClassifyArtifact(path)
            });
        }

        // Fallback format: bare filename followed by a code fence (e.g. "Design.md\n```markdown")
        if (artifacts.Count == 0)
        {
            var fallbackPattern = new Regex(
                @"^(?<path>[\w\.-]+\.(?:cs|md))\s*\n```\w*\s*\n(?<code>[\s\S]*?)\n```",
                RegexOptions.Multiline);

            foreach (Match match in fallbackPattern.Matches(input))
            {
                var path = match.Groups["path"].Value.Trim();
                var code = match.Groups["code"].Value.Trim();
                if (string.IsNullOrWhiteSpace(code)) continue;

                artifacts.Add(new CodeArtifact
                {
                    FileName = Path.GetFileName(path),
                    RelativePath = Path.GetDirectoryName(path) ?? "",
                    Content = CleanContent(code, path),
                    Type = ClassifyArtifact(path)
                });
            }
        }

        return artifacts;
    }

    private static async Task WriteArtifactsToSandboxAsync(List<CodeArtifact> artifacts)
    {
        Directory.CreateDirectory(SandboxDir);

        foreach (var pattern in new[] { "*.cs", "*.md" })
        {
            foreach (var existing in Directory.EnumerateFiles(SandboxDir, pattern, SearchOption.AllDirectories))
            {
                if (!existing.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                    File.Delete(existing);
            }
        }

        foreach (var artifact in artifacts)
        {
            var dir = Path.Combine(SandboxDir, artifact.RelativePath);
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, artifact.FileName), artifact.Content);
            Console.WriteLine($"[Sandbox] Wrote {artifact.FileName}");
        }

        Console.WriteLine($"[Sandbox] {artifacts.Count} file(s) written to {SandboxDir}");
    }

    private static string CleanContent(string code, string path)
    {
        // Remove non-ASCII garbage tokens produced by small LLMs
        code = Regex.Replace(code, @"[^\x00-\x7F]+", "");

        // For .cs files, truncate everything after the last closing brace to remove trailing prose
        if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            int lastBrace = code.LastIndexOf('}');
            if (lastBrace >= 0)
                code = code[..(lastBrace + 1)];
        }

        return code.TrimEnd();
    }

    private static async Task WriteDiagnosticFileAsync(string directory, string fileName, string content)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, fileName), content);
    }

    private static ArtifactType ClassifyArtifact(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return ArtifactType.SystemDesign;
        if (fileName.Contains("Test", StringComparison.OrdinalIgnoreCase))
            return ArtifactType.UnitTest;
        if (fileName.Contains("Service", StringComparison.OrdinalIgnoreCase))
            return ArtifactType.Service;
        if (fileName.Contains("Interface", StringComparison.OrdinalIgnoreCase)
            || (fileName.StartsWith('I') && fileName.Length > 1 && char.IsUpper(fileName[1])))
            return ArtifactType.Interface;
        if (fileName.Contains("Controller", StringComparison.OrdinalIgnoreCase))
            return ArtifactType.Controller;
        return ArtifactType.DomainModel;
    }
}

