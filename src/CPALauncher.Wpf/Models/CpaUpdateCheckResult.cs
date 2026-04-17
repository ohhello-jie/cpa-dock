namespace CPALauncher.Models;

public sealed class CpaUpdateCheckResult
{
    public required CpaUpdateCheckStatus Status { get; init; }

    public CpaUpdateInfo? UpdateInfo { get; init; }

    public string? LatestTagName { get; init; }

    public Version? LatestVersion { get; init; }

    public string? FailureReason { get; init; }

    public static CpaUpdateCheckResult UpdateAvailable(CpaUpdateInfo info) => new()
    {
        Status = CpaUpdateCheckStatus.UpdateAvailable,
        UpdateInfo = info,
        LatestTagName = info.TagName,
        LatestVersion = info.NewVersion,
    };

    public static CpaUpdateCheckResult UpToDate(string latestTagName, Version latestVersion) => new()
    {
        Status = CpaUpdateCheckStatus.UpToDate,
        LatestTagName = latestTagName,
        LatestVersion = latestVersion,
    };

    public static CpaUpdateCheckResult CheckFailed(string failureReason) => new()
    {
        Status = CpaUpdateCheckStatus.CheckFailed,
        FailureReason = failureReason,
    };
}
