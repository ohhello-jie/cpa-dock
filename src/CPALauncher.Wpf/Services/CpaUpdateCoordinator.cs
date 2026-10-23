namespace CPALauncher.Services;

public static class CpaUpdateCoordinator
{
    // Called only after download, extraction and backup have completed.
    public static async Task ApplyAsync(CpaUpdateTransaction transaction, Func<Task> stop,
        Func<bool, Task> start, Func<Task<bool>> isReady, Action<string?> saveVersion)
    {
        var stopped = false;
        var replaced = false;
        try
        {
            transaction.BeginActivation();
            await stop();
            stopped = true;
            transaction.Apply();
            replaced = true;
            await start(true);
            if (!await isReady()) throw new IOException("新版 CPA 未通过启动检查。");
            if (!transaction.WasRunning) await stop();
            saveVersion(transaction.NewVersion);
            transaction.Commit();
        }
        catch (Exception failure)
        {
            try
            {
                if (replaced) await stop();
                transaction.Rollback();
                saveVersion(transaction.PreviousVersion);
                if (stopped && transaction.WasRunning)
                {
                    await start(false);
                    if (!await isReady()) throw new IOException("旧版已恢复，服务尚未就绪。");
                }
                transaction.FinishRollback();
            }
            catch (Exception recovery)
            {
                throw new IOException($"{failure.Message} 恢复未完成：{recovery.Message} 恢复目录：{transaction.DirectoryPath}", recovery);
            }
            throw new IOException($"{failure.Message} 已恢复原版本。", failure);
        }
    }
}
