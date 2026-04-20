namespace CPALauncher.Services;

public sealed class TokenDropEvaluator
{
    public TokenDropEvaluationResult Evaluate(string? authDirectory, IReadOnlyList<string>? filePaths)
    {
        if (string.IsNullOrWhiteSpace(authDirectory))
        {
            return new TokenDropEvaluationResult
            {
                IsValid = false,
                Title = "当前未配置可用的认证目录",
                Subtitle = "请先解析 auth-dir 后再拖入凭证文件。",
            };
        }

        if (filePaths is null || filePaths.Count == 0)
        {
            return new TokenDropEvaluationResult
            {
                IsValid = false,
                Title = "未检测到可导入的凭证文件",
                Subtitle = "请拖入一个或多个本地 .json 文件。",
            };
        }

        if (filePaths.Any(Directory.Exists))
        {
            return new TokenDropEvaluationResult
            {
                IsValid = false,
                Title = "仅支持拖入本地 .json 文件",
                Subtitle = "拖拽内容中包含目录，请改为拖入单个 .json 文件。",
            };
        }

        if (filePaths.Any(filePath => IsInvalidJsonFile(filePath)))
        {
            return new TokenDropEvaluationResult
            {
                IsValid = false,
                Title = "仅支持导入 .json 凭证文件",
                Subtitle = "拖拽内容中包含非 .json 文件，请先筛选后再导入。",
            };
        }

        return new TokenDropEvaluationResult
        {
            IsValid = true,
            Title = "松手导入到当前 CPA 认证目录",
            Subtitle = authDirectory,
            TargetDirectory = authDirectory,
        };
    }

    private static bool IsInvalidJsonFile(string filePath)
        => string.IsNullOrWhiteSpace(filePath) || Directory.Exists(filePath) || !string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase);
}

public sealed class TokenDropEvaluationResult
{
    public bool IsValid { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string? TargetDirectory { get; init; }
}
