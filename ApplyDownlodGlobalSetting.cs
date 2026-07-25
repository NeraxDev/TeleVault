using System;
using System.Collections.Generic;
using System.Text;

namespace TeleVault
{
    public sealed partial class TeleService
    {
        private void ApplyDownlodGlobalSetting(TeleDownloadTask task, DownloadGlobalSettingsDTO global)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            if (global == null)
                throw new ArgumentNullException(nameof(global));

            //========================================
            // Threading
            //========================================

            if (task.policy.MaxThreads <= 0)
                task.policy.MaxThreads = task.Media.Size > 10 * 1024 * 1024 ? global.MaxThreads : 1;

            // اگر کاربر مقدار نداده باشد
            if (!task.policy.UseMultiThreaded)
                task.policy.UseMultiThreaded = task.Media.Size > 10 * 1024 * 1024;

            task.isOnMoving = false;

            //========================================
            // Network
            //========================================

            if (!task.policy.WaitForNetwork)
                task.policy.WaitForNetwork = global.WaitForNetwork;

            if (task.policy.waitForNetworkTimeout_sec <= 0)
                task.policy.waitForNetworkTimeout_sec =
                    global.WaitForNetworkTimeout_sec;

            if (task.policy.waitForNetworkRetryCount <= 0)
                task.policy.waitForNetworkRetryCount =
                    global.WaitForNetworkRetryCount;

            //========================================
            // Retry
            //========================================

            if (!task.policy.RetryOnError)
                task.policy.RetryOnError = global.RetryOnError;

            if (task.policy.MaxRetry <= 0)
                task.policy.MaxRetry = global.MaxRetry;

            if (task.policy.RetryDelay_sec <= 0)
                task.policy.RetryDelay_sec = global.RetryDelay_sec;

            //========================================
            // Disk
            //========================================

            if (!task.policy.MinimizeDiskIO)
                task.policy.MinimizeDiskIO = global.MinimizeDiskIO;
            //========================================
            // Paths
            //========================================
            task.TempPath = task.TempPath ?? global.TempPath;
            task.DestinationPath = task.DestinationPath ?? global.DestinationPath;
            //========================================
            // Runtime State
            // DO NOT TOUCH
            //========================================

            /*
                currentRetryCountForNetwork
                TimeUntilAutoStart
                TimeUntilAutoPause
                ScheduleDuration

                These are runtime values.
                They must keep default values.
            */
        }
    }
}