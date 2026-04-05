namespace GeniusAgent.Core.Models;

public record CodeArtifact
{
    public string FileName { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public ArtifactType Type { get; init; }
    public string FullPath => Path.Combine(RelativePath, FileName);
}

public enum ArtifactType { DomainModel, Interface, Service, Controller, UnitTest, SystemDesign }

public record ValidationResult(bool IsSuccess, List<string> Errors);

public class ComplianceOptions
{
    public bool SkipSandboxValidation { get; set; }
    public bool EnforceDesignDocs { get; set; } = true;
    public bool EnforceSourceCitations { get; set; }
}