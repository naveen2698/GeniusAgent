using System.Diagnostics;
using GeniusAgent.Core.Interfaces;
using GeniusAgent.Core.Models;

namespace GeniusAgent.Infrastructure.Services;

public class DotNetCliValidator : ICodeSandbox
{
    public async Task<ValidationResult> RunBuildAndTestAsync(IEnumerable<CodeArtifact> artifacts)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>";
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Sandbox.csproj"), csprojContent);

            foreach (var file in artifacts)
            {
                var path = Path.Combine(tempDir, file.RelativePath);
                Directory.CreateDirectory(path);
                await File.WriteAllTextAsync(Path.Combine(path, file.FileName), file.Content);
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"build {tempDir}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                return new ValidationResult(false, ["Failed to start dotnet build process."]);
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorsTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var output = await outputTask;
            var errors = await errorsTask;
            var combined = string.IsNullOrWhiteSpace(errors) ? output : $"{output}\n{errors}";
            return new ValidationResult(process.ExitCode == 0, [combined]);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup */ }
        }
    }
}