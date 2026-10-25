using System.Reflection;
using Xunit;

namespace CPALauncher.Wpf.Tests;

public sealed class SingleInstanceCoordinatorTests
{
    private static readonly Type Coordinator = typeof(CPALauncher.App).Assembly
        .GetType("CPALauncher.Services.SingleInstanceCoordinator")!;

    private static object Create() => Coordinator.GetMethod("Create")!.Invoke(null, null)!;
    private static bool IsPrimary(object value) => (bool)Coordinator.GetProperty("IsPrimary")!.GetValue(value)!;

    [Fact]
    public void Duplicate_UsesExistingInstance_AndPrimaryCanRestartAfterDispose()
    {
        var primary = Create();
        try
        {
            Assert.True(IsPrimary(primary));
            using var duplicate = (IDisposable)Create();
            Assert.False(IsPrimary(duplicate));
        }
        finally { ((IDisposable)primary).Dispose(); }

        using var restarted = (IDisposable)Create();
        Assert.True(IsPrimary(restarted));
    }

    [Fact]
    public void Duplicate_ForwardsActivationArguments_WithoutStartingAnotherPrimary()
    {
        using var primary = (IDisposable)Create();
        Assert.True(IsPrimary(primary));
        using var received = new ManualResetEventSlim();
        string[]? receivedArgs = null;
        Coordinator.GetMethod("StartListening")!.Invoke(primary,
            [(Action<string[]>)(args => { receivedArgs = args; received.Set(); })]);
        using var duplicate = (IDisposable)Create();
        Assert.False(IsPrimary(duplicate));
        string[] args = ["--minimized", "sample"];
        Coordinator.GetMethod("RequestActivation")!.Invoke(duplicate, [args]);
        Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "Existing instance did not receive activation.");
        Assert.Equal(args, receivedArgs);
    }
}
