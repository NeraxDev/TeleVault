using NeraXTools;
using NeraXTools.LogManager;
using Newtonsoft.Json.Linq;
using System.IO;

namespace TeleVault
{
    public sealed class DownloadGlobalSettingsDTO
    {
        public int MaxThreads { get; set; } = 8;

        //public bool UseMultiThreaded { get; set; } = true; // This property is not needed anymore, becuse if user not seted this property, This auto matically detect by Size of file .

        public bool WaitForNetwork { get; set; } = true;
        public int waitForNetworkDelay_sec { get; set; } = 30;
        public int WaitForNetworkRetryCount { get; set; } = 5;
        public bool RetryOnError { get; set; } = true;
        public int MaxRetry { get; set; } = 3;
        public int RetryDelay_sec { get; set; } = 5;
        public int MaxFail { get; set; } = 1;
        public bool AddToRearOfQueueAfterFailure { get; set; } = false;
        public bool RemoveFromQueueAfterFailure { get; set; } = false;
        public bool MinimizeDiskIO { get; set; } = false;

        //----------------------------

        //public string TempFileName  // Dont Need This becuse this temp file name if not seted with user in 'Applay Downlod GLobal Setting', This will be auto generated an numbric File Name  + Data Time Now with out extension. For example: 482913_20260725_071530

        private string _tempPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeraX", "TeleVault", "Temp");

        public string TempPath
        {
            get
            {
                if (_tempPath != null)
                {
                    if (!Directory.Exists(_tempPath)) // TODO: Must be replaced with internal NeraXTools utility  // TODO : باید با ابزار داخلی نیراکس تولز رپلیس شود
                    {
                        try
                        {
                            var r = FolderOps.CreateFolder(_tempPath);
                            if (r != null && r.Success)
                                return _tempPath;
                            else throw new Exception("Failed to create temp folder");
                        }
                        catch { return null; }
                    }
                    return _tempPath;
                }
                return null;
            }
            set
            {
                if (value != null)
                {
                    if (!Directory.Exists(value)) // TODO: Must be replaced with internal NeraXTools utility  // TODO : باید با ابزار داخلی نیراکس تولز رپلیس شود
                    {
                        try
                        {
                            var r = FolderOps.CreateFolder(value);
                            if (r != null && r.Success)
                                _tempPath = value;
                        }
                        catch { }
                    }
                    else
                        _tempPath = value;
                }
            }
        }

        private string _destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TeleVault");

        public string DestinationPath
        {
            get
            {
                if (_destinationPath != null)
                {
                    if (!Directory.Exists(_destinationPath)) // TODO: Must be replaced with internal NeraXTools utility  // TODO : باید با ابزار داخلی نیراکس تولز رپلیس شود
                    {
                        try
                        {
                            var r = FolderOps.CreateFolder(_destinationPath);
                            if (r != null && r.Success)
                                return _destinationPath;
                            else throw new Exception("Failed to create destination folder");
                        }
                        catch { return null; }
                    }
                    return _destinationPath;
                }
                return null;
            }
            set
            {
                if (value != null)
                {
                    if (!Directory.Exists(value)) // TODO: Must be replaced with internal NeraXTools utility  // TODO : باید با ابزار داخلی نیراکس تولز رپلیس شود
                    {
                        try
                        {
                            var r = FolderOps.CreateFolder(value);
                            if (r != null && r.Success)
                                _destinationPath = value;
                        }
                        catch { }
                    }
                    else
                        _destinationPath = value;
                }
            }
        }

        private SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3);

        internal SemaphoreSlim GetDownloadSemaphore { get => _downloadSemaphore; }

        internal void SetDownloadSemaphore(int value)
        {
            if (value > 0)
            {
                GetDownloadSemaphore.Dispose();
                _downloadSemaphore = new SemaphoreSlim(value);
            }
            else throw new ArgumentOutOfRangeException(nameof(value));
        }

        public eDownloadChunkSize SetChunkSize { set => GetChunkSizeValue = (int)value * 1024 * 128; }

        public int GetChunkSizeValue { get; private set; } = (int)eDownloadChunkSize.MB_1 * 1024 * 128; // Default chunk size for downloads 1 MB

        private string _tempFileExtension = "nxtem";

        /// <summary> without dot, for example: "nxtem" or "tempfile"</summary>
        public string TempFileExtension
        {
            get => _tempFileExtension;
            set
            {
                if (!string.IsNullOrEmpty(_tempFileExtension))
                {
                    Logger.log("TempFileExtension is already set. It cannot be changed.", eLogType.Warning, eLogRecordMode.UI);
                    return;
                }
                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.log("Null or whitespace value provided for TempFileName. It cannot be changed.", eLogType.Warning, eLogRecordMode.UI);
                    return;
                }
                _tempFileExtension = value.Trim().Trim('.');
            }
        }  // Default file extension for downloaded files

        private int _rollbackFactor = 3;

        public int RollbackFactor
        {
            get => _rollbackFactor;
            set => _rollbackFactor = Math.Clamp(value, 1, 10);
        }

        public int GetRollbackSize(eDownloadChunkSize chunkSize) => ((int)chunkSize * 1024 * 128) * RollbackFactor;

        //--
    }
}