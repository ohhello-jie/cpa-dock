using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class TokenDropEvaluatorTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "cpa-launcher-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Evaluate_WhenAuthDirectoryIsEmpty_ReturnsRejectedResult()
    {
        var evaluator = new TokenDropEvaluator();

        var result = evaluator.Evaluate(string.Empty, ["a.json"]);

        Assert.False(result.IsValid);
        Assert.Equal("当前未配置可用的认证目录", result.Title);
        Assert.Equal("请先解析 auth-dir 后再拖入凭证文件。", result.Subtitle);
        Assert.Null(result.TargetDirectory);
    }

    [Fact]
    public void Evaluate_WhenFileListIsEmpty_ReturnsRejectedResult()
    {
        var evaluator = new TokenDropEvaluator();

        var result = evaluator.Evaluate(@"D:\auth", Array.Empty<string>());

        Assert.False(result.IsValid);
        Assert.Equal("未检测到可导入的凭证文件", result.Title);
        Assert.Null(result.TargetDirectory);
    }

    [Fact]
    public void Evaluate_WhenFilesContainDirectory_ReturnsRejectedResult()
    {
        var evaluator = new TokenDropEvaluator();
        var folder = Directory.CreateDirectory(Path.Combine(tempRoot, "tokens")).FullName;

        var result = evaluator.Evaluate(@"D:\auth", [folder]);

        Assert.False(result.IsValid);
        Assert.Equal("仅支持拖入本地 .json 文件", result.Title);
        Assert.Null(result.TargetDirectory);
    }

    [Fact]
    public void Evaluate_WhenFilesContainNonJson_ReturnsRejectedResult()
    {
        var evaluator = new TokenDropEvaluator();

        var result = evaluator.Evaluate(@"D:\auth", ["note.txt"]);

        Assert.False(result.IsValid);
        Assert.Equal("仅支持导入 .json 凭证文件", result.Title);
        Assert.Null(result.TargetDirectory);
    }

    [Fact]
    public void Evaluate_WhenFilesAreValid_ReturnsAcceptableResult()
    {
        var evaluator = new TokenDropEvaluator();
        var authDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "auth")).FullName;

        var result = evaluator.Evaluate(authDirectory, ["one.json", "two.JSON"]);

        Assert.True(result.IsValid);
        Assert.Equal("松手导入到当前 CPA 认证目录", result.Title);
        Assert.Equal(authDirectory, result.TargetDirectory);
        Assert.Equal(authDirectory, result.Subtitle);
    }

    [Fact]
    public void Evaluate_WhenSelfDragAndExternalSameNameAreMixed_ReturnsAcceptableResult()
    {
        var evaluator = new TokenDropEvaluator();
        var authDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "auth")).FullName;
        var selfDragFile = Path.Combine(authDirectory, "token.json");
        File.WriteAllText(selfDragFile, """{"token":"existing"}""");
        var externalFile = CreateJsonFile(Path.Combine(tempRoot, "source"), "token.json");

        var result = evaluator.Evaluate(authDirectory, [selfDragFile, externalFile]);

        Assert.True(result.IsValid);
        Assert.Equal("松手导入到当前 CPA 认证目录", result.Title);
        Assert.Equal(authDirectory, result.TargetDirectory);
    }

    [Fact]
    public void Evaluate_WhenTwoExternalFilesResolveToSameTargetName_ReturnsRejectedResult()
    {
        var evaluator = new TokenDropEvaluator();
        var authDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "auth")).FullName;
        var firstFile = CreateJsonFile(Path.Combine(tempRoot, "source-one"), "token.json");
        var secondFile = CreateJsonFile(Path.Combine(tempRoot, "source-two"), "token.json");

        var result = evaluator.Evaluate(authDirectory, [firstFile, secondFile]);

        Assert.False(result.IsValid);
        Assert.Equal("拖拽内容存在重复的目标文件名", result.Title);
        Assert.Contains("token.json", result.Subtitle);
        Assert.Null(result.TargetDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private string CreateJsonFile(string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, """{"token":"value"}""");
        return path;
    }
}
