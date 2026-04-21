using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class TokenImportServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "cpa-launcher-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ImportJsonFiles_WhenAuthDirectoryIsEmpty_RejectsBatch()
    {
        var service = new TokenImportService();

        var result = service.ImportJsonFiles(string.Empty, [CreateJsonFile("source.json")]);

        Assert.Empty(result.OverwrittenFiles);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal("当前未配置可用的认证目录。", result.SummaryMessage);
        Assert.Equal(["当前未配置可用的认证目录。"], result.Errors);
    }

    [Fact]
    public void ImportJsonFiles_WhenSourceFilesAreEmpty_RejectsBatch()
    {
        var service = new TokenImportService();
        var authDirectory = Path.Combine(tempRoot, "auth");

        var result = service.ImportJsonFiles(authDirectory, Array.Empty<string>());

        Assert.Empty(result.OverwrittenFiles);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal("未检测到可导入的凭证文件。", result.SummaryMessage);
        Assert.Equal(["未检测到可导入的凭证文件。"], result.Errors);
    }

    [Fact]
    public void ImportJsonFiles_WhenTargetDirectoryDoesNotExist_CreatesDirectoryAndImportsFile()
    {
        var service = new TokenImportService();
        var authDirectory = Path.Combine(tempRoot, "auth");
        var sourceFile = CreateJsonFile("one.json", """
            {"token":"alpha"}
            """);

        var result = service.ImportJsonFiles(authDirectory, [sourceFile]);

        Assert.True(Directory.Exists(authDirectory));
        Assert.True(File.Exists(Path.Combine(authDirectory, "one.json")));
        Assert.Equal("""
            {"token":"alpha"}
            """, File.ReadAllText(Path.Combine(authDirectory, "one.json")));
        Assert.Equal(TokenImportStatus.Succeeded, result.Status);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Empty(result.OverwrittenFiles);
        Assert.Empty(result.Errors);
        Assert.Contains("已导入 1 个凭证文件", result.SummaryMessage);
    }

    [Fact]
    public void ImportJsonFiles_WhenMultipleJsonFilesProvided_ImportsAllFiles()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var firstFile = CreateJsonFile("first.json", """{"token":"first"}""");
        var secondFile = CreateJsonFile("second.JSON", """{"token":"second"}""");

        var result = service.ImportJsonFiles(authDirectory, [firstFile, secondFile]);

        Assert.Equal(TokenImportStatus.Succeeded, result.Status);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Empty(result.OverwrittenFiles);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(Path.Combine(authDirectory, "first.json")));
        Assert.True(File.Exists(Path.Combine(authDirectory, "second.JSON")));
    }

    [Fact]
    public void ImportJsonFiles_WhenSourceFileAlreadyExists_OverwritesExistingFile()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        File.WriteAllText(Path.Combine(authDirectory, "token.json"), """{"token":"old"}""");
        var sourceFile = CreateJsonFile("token.json", """{"token":"new"}""");

        var result = service.ImportJsonFiles(authDirectory, [sourceFile]);

        Assert.Equal(TokenImportStatus.Succeeded, result.Status);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(["token.json"], result.OverwrittenFiles);
        Assert.Equal("""{"token":"new"}""", File.ReadAllText(Path.Combine(authDirectory, "token.json")));
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ImportJsonFiles_WhenInputContainsNonJsonFile_RejectsEntireBatch()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var jsonFile = CreateJsonFile("one.json");
        var textFile = CreateTextFile("two.txt");

        var result = service.ImportJsonFiles(authDirectory, [jsonFile, textFile]);

        Assert.Equal(TokenImportStatus.Rejected, result.Status);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal("仅支持导入 .json 凭证文件。", result.SummaryMessage);
        Assert.Equal(["仅支持导入 .json 凭证文件。"], result.Errors);
        Assert.False(File.Exists(Path.Combine(authDirectory, "one.json")));
        Assert.False(File.Exists(Path.Combine(authDirectory, "two.txt")));
    }

    [Fact]
    public void ImportJsonFiles_WhenOneFileCopyFails_ContinuesImportingRemainingFiles()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var goodFile = CreateJsonFile("good.json", """{"token":"good"}""");
        var missingFile = Path.Combine(tempRoot, "missing.json");

        var result = service.ImportJsonFiles(authDirectory, [goodFile, missingFile]);

        Assert.Equal(TokenImportStatus.PartiallySucceeded, result.Status);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Single(result.Errors);
        Assert.True(File.Exists(Path.Combine(authDirectory, "good.json")));
        Assert.Equal("已导入 1 个凭证文件，1 个文件失败。", result.SummaryMessage);
        Assert.Contains("missing.json", result.Errors[0]);
    }

    [Fact]
    public void ImportJsonFiles_WhenSourceContainsDirectory_RejectsEntireBatch()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var folder = CreateDirectory("folder.json");
        var jsonFile = CreateJsonFile("ok.json");

        var result = service.ImportJsonFiles(authDirectory, [jsonFile, folder]);

        Assert.Equal(TokenImportStatus.Rejected, result.Status);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal("仅支持导入 .json 凭证文件。", result.SummaryMessage);
        Assert.Equal(["仅支持导入 .json 凭证文件。"], result.Errors);
    }

    [Fact]
    public void ImportJsonFiles_WhenBatchContainsDuplicateFileNames_RejectsEntireBatch()
    {
        var service = new TokenImportService();
        var authDirectory = Path.Combine(tempRoot, "auth");
        var firstFile = CreateJsonFile("token.json", """{"token":"first"}""");
        var secondFile = CreateJsonFile("token.json", """{"token":"second"}""");

        var result = service.ImportJsonFiles(authDirectory, [firstFile, secondFile]);

        Assert.Equal(TokenImportStatus.Rejected, result.Status);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Empty(result.OverwrittenFiles);
        Assert.Contains("重复的目标文件名", result.SummaryMessage);
        Assert.Equal(result.SummaryMessage, result.Errors[0]);
        Assert.False(Directory.Exists(authDirectory));
    }

    [Fact]
    public void ImportJsonFiles_WhenBatchContainsSelfDragAndExternalSameName_AllowsImport()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var selfDragFile = Path.Combine(authDirectory, "token.json");
        File.WriteAllText(selfDragFile, """{"token":"existing"}""");
        var externalFile = CreateJsonFile("token.json", """{"token":"external"}""");

        var result = service.ImportJsonFiles(authDirectory, [selfDragFile, externalFile]);

        Assert.Equal(TokenImportStatus.Succeeded, result.Status);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(["token.json"], result.SkippedFiles);
        Assert.Empty(result.Errors);
        Assert.Equal("""{"token":"external"}""", File.ReadAllText(Path.Combine(authDirectory, "token.json")));
    }

    [Fact]
    public void ImportJsonFiles_WhenAuthDirectoryCannotBePrepared_ReturnsRejectedResult()
    {
        var service = new TokenImportService();
        var parentDirectory = CreateDirectory("prepare-failure");
        var authDirectory = Path.Combine(parentDirectory, "locked-target");
        File.WriteAllText(authDirectory, "not a directory");
        var sourceFile = CreateJsonFile("one.json");

        var result = service.ImportJsonFiles(authDirectory, [sourceFile]);

        Assert.Equal(TokenImportStatus.Rejected, result.Status);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Contains("创建认证目录失败", result.SummaryMessage);
        Assert.Equal(result.SummaryMessage, result.Errors[0]);
    }

    [Fact]
    public void ImportJsonFiles_WhenSourceAndTargetAreSamePath_DoesNotCountAsSuccess()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var sourceFile = Path.Combine(authDirectory, "token.json");
        File.WriteAllText(sourceFile, """{"token":"existing"}""");

        var result = service.ImportJsonFiles(authDirectory, [sourceFile]);

        Assert.Equal(TokenImportStatus.NoOp, result.Status);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Single(result.SkippedFiles);
        Assert.Equal(["token.json"], result.SkippedFiles);
        Assert.Empty(result.Errors);
        Assert.Equal("""{"token":"existing"}""", File.ReadAllText(sourceFile));
    }

    [Fact]
    public void ImportJsonFiles_WhenAllCopiesFail_AreReportedAsFailed()
    {
        var service = new TokenImportService();
        var authDirectory = CreateDirectory("auth");
        var missingOne = Path.Combine(tempRoot, "missing-one.json");
        var missingTwo = Path.Combine(tempRoot, "missing-two.json");

        var result = service.ImportJsonFiles(authDirectory, [missingOne, missingTwo]);

        Assert.Equal(TokenImportStatus.Failed, result.Status);
        Assert.Equal(0, result.ImportedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, error => error.Contains("missing-one.json"));
        Assert.Contains(result.Errors, error => error.Contains("missing-two.json"));
        Assert.Contains("2 个文件导入失败", result.SummaryMessage);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private string CreateDirectory(string name)
    {
        var path = Path.Combine(tempRoot, name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private string CreateJsonFile(string fileName, string? content = null)
    {
        var directory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content ?? """{"token":"sample"}""");
        return path;
    }

    private string CreateTextFile(string fileName, string content = "plain text")
    {
        var directory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}
