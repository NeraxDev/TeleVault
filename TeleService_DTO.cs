using System.IO;
using NeraXTools;

namespace TeleVault
{
    public sealed class DownloadGlobalSettingsDTO
    {
        public int MaxThreads { get; set; } = 8;

        //public bool UseMultiThreaded { get; set; } = true; // This property is not needed anymore, becuse if user not seted this property, This auto matically detect by Size of file .

        public bool WaitForNetwork { get; set; } = true;
        public int WaitForNetworkTimeout_sec { get; set; } = 30;
        public int WaitForNetworkRetryCount { get; set; } = 5;
        public bool RetryOnError { get; set; } = true;
        public int MaxRetry { get; set; } = 3;
        public int RetryDelay_sec { get; set; } = 5;
        public bool MinimizeDiskIO { get; set; } = false;

        private string TempPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeraX", "TeleVault", "Temp");
        private string _destinationPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TeleVault");

        public string DestinationPath
        {
            get;
            set
            {
                if (_destinationPath != null)
                {
                    if (!Directory.Exists(value)) // TODO: Must be replaced with internal NeraXTools utility  // TODO : باید با ابزار داخلی نیراکس تولز رپلیس شود
                    {
                        try
                        {
                            FolderOps.CreateFolder(value);
                        }
                        catch
                        {
                            _destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TeleVault");
                            return;
                        }
                    }
                    _destinationPath = value;
                }
            }
        }

        private SemaphoreSlim _downloadSemaphore { get; set; } = new SemaphoreSlim(3);

        public int DownloadSemaphore
        {
            get;
            set
            {
                if (value > 0)
                    _downloadSemaphore = new SemaphoreSlim(value);
                else
                    _downloadSemaphore = new SemaphoreSlim(1);
            }
        }
    }
}