namespace CPALauncher.Services;

public sealed class TokenImportService
{
    public TokenImportResult ImportJsonFiles(string? authDirectory, IEnumerable<string>? sourceFiles)
    {
        if (string.IsNullOrWhiteSpace(authDirectory))
        {
            return TokenImportResult.Rejected("当前未配置可用的认证目录。");
        }

        if (sourceFiles is null)
        {
            return TokenImportResult.Rejected("未检测到可导入的凭证文件。");
        }

        var files = sourceFiles.ToArray();
        if (files.Length == 0)
        {
            return TokenImportResult.Rejected("未检测到可导入的凭证文件。");
        }

        if (files.Any(file => string.IsNullOrWhiteSpace(file) || IsInvalidSourceFile(file)))
        {
            return TokenImportResult.Rejected("仅支持导入 .json 凭证文件。");
        }

        Directory.CreateDirectory(authDirectory);

        var importedCount = 0;
        var failedCount = 0;
        var errors = new List<string>();
        var overwrittenFiles = new List<string>();

        foreach (var sourceFile in files)
        {
            var fileName = Path.GetFileName(sourceFile);
            var targetPath = Path.Combine(authDirectory, fileName);

            try
            {
                if (PathsReferToSameFile(sourceFile, targetPath))
                {
                    importedCount++;
                    continue;
                }

                var willOverwrite = File.Exists(targetPath);
                File.Copy(sourceFile, targetPath, overwrite: true);

                if (willOverwrite)
                {
                    overwrittenFiles.Add(fileName);
                }

                importedCount++;
            }
            catch (Exception ex)
            {
                failedCount++;
                errors.Add($"导入 {fileName} 失败：{ex.Message}");
            }
        }

        return new TokenImportResult
        {
            ImportedCount = importedCount,
            FailedCount = failedCount,
            Errors = errors,
            OverwrittenFiles = overwrittenFiles,
            SummaryMessage = BuildSummaryMessage(importedCount, failedCount),
        };
    }

    private static bool IsInvalidSourceFile(string sourceFile)
        => Directory.Exists(sourceFile) || !string.Equals(Path.GetExtension(sourceFile), ".json", StringComparison.OrdinalIgnoreCase);

    private static bool PathsReferToSameFile(string sourceFile, string targetPath)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(sourceFile),
                Path.GetFullPath(targetPath),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSummaryMessage(int importedCount, int failedCount)
    {
        return failedCount == 0
            ? $"已导入 {importedCount} 个凭证文件。"
            : $"已导入 {importedCount} 个凭证文件，{failedCount} 个文件失败。";
    }
}

public sealed class TokenImportResult
{
    public int ImportedCount { get; init; }

    public int FailedCount { get; init; }

    public List<string> Errors { get; init; } = [];

    public List<string> OverwrittenFiles { get; init; } = [];

    public string SummaryMessage { get; init; } = string.Empty;

    public static TokenImportResult Rejected(string message) => new()
    {
        Errors = [message],
        SummaryMessage = message,
    };
}
