using System.Diagnostics;

namespace Orpheus.Tests.Integration;

public class DockerBuildTests
{
    [Fact]
    public void DockerFile_Exists()
    {
        // Arrange
        var dockerfilePath = Path.Combine(GetProjectRoot(), "Dockerfile");

        // Act & Assert
        Assert.True(File.Exists(dockerfilePath), $"Dockerfile should exist at {dockerfilePath}");
    }

    [Fact]
    public void DockerFile_ContainsRequiredStages()
    {
        // Arrange
        var dockerfilePath = Path.Combine(GetProjectRoot(), "Dockerfile");
        var dockerfileContent = File.ReadAllText(dockerfilePath);

        // Act & Assert
        Assert.Contains("FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base", dockerfileContent);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build", dockerfileContent);
        Assert.Contains("FROM build AS publish", dockerfileContent);
        Assert.Contains("FROM base AS final", dockerfileContent);
    }

    [Fact]
    public void DockerFile_InstallsRequiredDependencies()
    {
        // Arrange
        var dockerfilePath = Path.Combine(GetProjectRoot(), "Dockerfile");
        var dockerfileContent = File.ReadAllText(dockerfilePath);

        // Act & Assert
        Assert.Contains("apt-get install", dockerfileContent);
        Assert.Contains("python3", dockerfileContent);
        Assert.Contains("ffmpeg", dockerfileContent);
        Assert.Contains("yt-dlp", dockerfileContent);
        Assert.Contains("libopus", dockerfileContent);
    }

    [Fact]
    public void DockerFile_ConfiguresEntrypoint()
    {
        // Arrange
        var dockerfilePath = Path.Combine(GetProjectRoot(), "Dockerfile");
        var dockerfileContent = File.ReadAllText(dockerfilePath);

        // Act & Assert
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Orpheus.dll\"]", dockerfileContent);
    }

    [Fact(Skip = "Requires Docker to be installed and running - enable for CI environments")]
    public void Docker_BuildSucceeds()
    {
        // This test is skipped by default as it requires Docker to be running
        // Enable this test in CI environments where Docker is available
        
        // Arrange
        var projectRoot = GetProjectRoot();
        
        // Act
        var result = RunDockerBuild(projectRoot);
        
        // Assert
        Assert.Equal(0, result.ExitCode);
    }

    private static string GetProjectRoot()
    {
        // Start from the test assembly location and walk up to find the project root
        var currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
        
        while (currentDirectory != null)
        {
            // Look for the main project file to identify the root
            if (File.Exists(Path.Combine(currentDirectory, "Orpheus.csproj")))
            {
                return currentDirectory;
            }
            
            var parentDirectory = Directory.GetParent(currentDirectory);
            if (parentDirectory == null)
                break;
                
            currentDirectory = parentDirectory.FullName;
        }
        
        // Fallback: assume we're in the standard test structure within the main project
        var testDir = Path.GetDirectoryName(typeof(DockerBuildTests).Assembly.Location);
        return Path.GetFullPath(Path.Combine(testDir!, "..", "..", "..", ".."));
    }

    private static ProcessResult RunDockerBuild(string contextPath)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"build --no-cache -t orpheus-test .",
            WorkingDirectory = contextPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(processStartInfo);
        process?.WaitForExit(TimeSpan.FromMinutes(10)); // 10 minute timeout for Docker build

        return new ProcessResult
        {
            ExitCode = process?.ExitCode ?? -1,
            StandardOutput = process?.StandardOutput.ReadToEnd() ?? string.Empty,
            StandardError = process?.StandardError.ReadToEnd() ?? string.Empty
        };
    }

    private record ProcessResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
    }
}