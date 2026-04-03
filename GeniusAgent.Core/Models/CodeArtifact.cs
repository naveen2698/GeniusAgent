namespace GeniusAgent.Core.Models;

/// <summary>
/// Represents a code artifact, including its file name, relative path, content, and type.
/// </summary>
/// <remarks>Use this record to encapsulate information about a single code artifact, such as a source file or
/// resource, within a project or repository. The artifact's full path is derived from its relative path and file
/// name.</remarks>
public record CodeArtifact
{
    /// <summary>
    /// The file name of the code artifact.
    /// </summary>
    public string FileName { get; init; } = string.Empty;
    /// <summary>
    /// The relative path of the code artifact within the project or repository.
    /// </summary>
    public string RelativePath { get; init; } = string.Empty;
    /// <summary>
    /// The content of the code artifact.
    /// </summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>
    /// The type of the code artifact.
    /// </summary>
    public ArtifactType Type { get; init; }
    /// <summary>
    /// The full path of the code artifact, which is derived from its relative path and file name.
    /// </summary>
    public string FullPath => Path.Combine(RelativePath, FileName);
}

/// <summary>
/// Represents the type of a code artifact.
/// </summary>
public enum ArtifactType { Undefined, DomainModel, Dto, Interface, Service, Repository, Controller, UnitTest }

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
/// <param name="IsSuccess"></param>
/// <param name="Errors"></param>
public record ValidationResult(bool IsSuccess, List<string> Errors);