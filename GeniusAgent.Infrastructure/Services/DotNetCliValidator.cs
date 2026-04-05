using System.Diagnostics;
using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;

namespace GeniusAgent.Infrastructure.Services;

public class DotNetCliValidator : ICodeSandbox
{
    private static readonly string SandboxDir = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "SandboxProject"));

    public async Task<ValidationResult> RunBuildAndTestAsync(IEnumerable<CodeArtifact> artifacts)
    {
        if (Directory.Exists(SandboxDir))
        {
            try { Directory.Delete(SandboxDir, recursive: true); } catch { }
        }
        Directory.CreateDirectory(SandboxDir);

        var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Microsoft.NET.Test.Sdk"" Version=""17.8.0"" />
    <PackageReference Include=""xunit"" Version=""2.6.4"" />
    <PackageReference Include=""xunit.runner.visualstudio"" Version=""2.5.6"" />
    <PackageReference Include=""Moq"" Version=""4.20.70"" />
  </ItemGroup>
</Project>";
        await File.WriteAllTextAsync(Path.Combine(SandboxDir, "Sandbox.csproj"), csprojContent);

        foreach (var file in artifacts)
        {
            var path = Path.Combine(SandboxDir, file.RelativePath);
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, file.FileName), file.Content);
        }

        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test {SandboxDir} --logger \"console;verbosity=detailed\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });

        if (process is null)
            return new ValidationResult(false, ["Failed to start dotnet build process."]);

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorsTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var errors = await errorsTask;
        var combined = string.IsNullOrWhiteSpace(errors) ? output : $"{output}\n{errors}";
        return new ValidationResult(process.ExitCode == 0, [combined]);
    }
}