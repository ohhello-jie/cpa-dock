namespace CPALauncher.Services;

public static class DiagnosticLogFilter
{
    private static readonly string[] ErrorWords = ["error", "failed", "fatal", "panic", "stderr", "失败", "错误"];
    public static bool Matches(string line, string search, int scope)
    {
        if (!string.IsNullOrWhiteSpace(search)
            && !line.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        return scope switch
        {
            1 => ErrorWords
                .Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)),
            2 => line.Contains("launcher", StringComparison.OrdinalIgnoreCase),
            3 => line.Contains("stdout", StringComparison.OrdinalIgnoreCase)
                 || line.Contains("stderr", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };
    }
}
