using CPALauncher.Services;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class DiagnosticLogFilterTests
{
    [Theory]
    [InlineData("stdout | ERROR binding port 8317", "8317", 1, true)]
    [InlineData("stdout | ERROR binding port 8317", "8318", 1, false)]
    [InlineData("[launcher] 启动失败", "", 1, true)]
    [InlineData("stdout | ready", "", 1, false)]
    [InlineData("[launcher] ready", "READY", 2, true)]
    [InlineData("stdout | ready", "", 2, false)]
    [InlineData("stderr | error", " error ", 3, true)]
    [InlineData("[launcher] ready", "", 3, false)]
    public void ScopeAndSearch_AreAppliedTogether(string line, string search, int scope, bool expected)
        => Assert.Equal(expected, DiagnosticLogFilter.Matches(line, search, scope));
}
