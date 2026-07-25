using NeraXTools;
using NeraXTools.LogManager;
using Newtonsoft.Json.Linq;
using System.Data;
using System.IO;
using TL;

namespace TeleVault
{
    public sealed class TeleMediaInfo
    {
        public string FileName { get; set; }
        public long Id { get; set; }
        public long AccessHash { get; set; }
        public byte[] FileReference { get; set; }
        public long Size { get; set; }
        public int DcId { get; set; }
        public eTeleMediaType MediaType { get; set; }
        public InputFileLocationBase Location { get; set; }
    }

    public class TeleDownloadTask
    {
        public TeleMediaInfo Media { get; init; }
        public string FileName { get; set; } = string.Empty;  // This should be set file Name In use time of download.

        public string FileExtension { get; set; } = string.Empty; // This should be set file extension In use time of download.

        public string FullPath
        {
            get
            {
                if (_destinationPath != null && FileName != null && FileExtension != null)
                {
                    return Path.Combine(_destinationPath, $"{FileName}.{FileExtension}");
                }
                return null;
            }
        }

        private string _fullTempFilePath = null; // This is the full path to the temporary file used during download. It should be set to a valid path that matches the FileName and set app Extension.

        public string FullTempFilePath
        {
            get
            {
                try
                {
                    if (_fullTempFilePath != null && Path.GetDirectoryName(_fullTempFilePath).Trim() == FileName)
                    {
                        return _fullTempFilePath;
                    }
                    throw new Exception("Temp file path is not set or does not match the file name.");
                }
                catch (Exception ex)
                {
                    //Logger.log(ex.ToString(), eLogType.Error, eLogRecordMode.UI);
                    return null;
                }
            }
            set
            {
                try
                {
                    if (value != null && Path.GetDirectoryName(value).Trim() == FileName)
                    {
                        _fullTempFilePath = value;
                    }
                    else
                    {
                        throw new Exception("Temp file path is not set or does not match the file name.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.log(ex.ToString(), eLogType.Error, eLogRecordMode.UI);
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

        public eTeleMediaDownloadStatus Status { get; set; } = eTeleMediaDownloadStatus.NotStarted;
        public bool isOnMoving { get; set; } = false; // برا وقتی که دانلود تموم شدده و دانلود بیت ریست شده و برا انتقال کار میکنه کاربرد داره
        public long DownloadedBytes { get; set; } // این دو کابرد داره یکی زمان دانلود و حالت دوم زمانی که فایل دارد انتقال پیدا میکنه
        public string StateProgress => Media.Size > 0 ? $"{(DownloadedBytes * 100.0 / Media.Size):F2}%" : "0%"; // اینم دو کاربرد داره

        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        private List<TeleDownloadChunk> _chunks = new();

        public List<TeleDownloadChunk> Chunks
        {
            get => _chunks;

            set
            {
                if (value == null || !value.Any())
                {
                    _chunks = new List<TeleDownloadChunk>();
                    return;
                }
                _chunks = value;
            }
        }

        public DownloadPolicy policy { get; init; }
    }

    public sealed class TeleDownloadChunk
    {
        public long StartOffset { get; init; }
        public long EndOffset { get; init; }
        public long DownloadedBytes { get; set; }
        public string DownloadProgress => (DownloadedBytes * 100.0 / (EndOffset - StartOffset + 1)).ToString("F2") + "%";
        public eTeleMediaDownloadStatus Status { get; set; }
    }

    public class DownloadPolicy
    {
        // تنظیمات تردینگ (می‌تواند جایگزین Multi/Single شود)
        public int MaxThreads { get; set; }

        public bool UseMultiThreaded { get; set; }

        // تنظیماتِ خودکارسازی (Auto-Resume)
        public bool WaitForNetwork { get; set; }

        public int waitForNetworkTimeout_sec { get; set; }
        public int waitForNetworkRetryCount { get; set; }

        // -------- اینا برا نمایش لحظه ای کاربر هستن و نباید توی تنظیمات ذخیره بشن
        public int currentRetryCountForNetwork { get; set; } = 0;

        //------------------------------------------------

        /// <summary>
        /// Enable automatic start/pause scheduling.
        /// </summary>
        public bool UseSchedule { get; set; }

        /// <summary>
        /// Automatic start date and time.
        /// Set using CreateDateTime Method or directly.
        /// </summary>
        public DateTime AutoStartDateTime { get; set; }

        /// <summary>
        /// Automatic pause date and time.
        /// Set using CreateDateTime Method or directly.
        /// </summary>
        public DateTime AutoPauseDateTime { get; set; }

        // -------- اینا برا نمایش لحظه ای کاربر هستن و نباید توی تنظیمات ذخیره بشن
        /// <summary>
        /// Remaining time until automatic start.
        /// Returns TimeSpan.Zero if schedule is disabled or date is not configured.
        /// </summary>
        public TimeSpan TimeUntilAutoStart
        {
            get
            {
                if (!UseSchedule ||
                    AutoStartDateTime == DateTime.MinValue)
                    return TimeSpan.Zero;

                TimeSpan remaining = AutoStartDateTime - DateTime.Now;

                return remaining > TimeSpan.Zero
                    ? remaining
                    : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Remaining time until automatic pause.
        /// Returns TimeSpan.Zero if schedule is disabled or date is not configured.
        /// </summary>
        public TimeSpan TimeUntilAutoPause
        {
            get
            {
                if (!UseSchedule ||
                    AutoPauseDateTime == DateTime.MinValue)
                    return TimeSpan.Zero;

                TimeSpan remaining = AutoPauseDateTime - DateTime.Now;

                return remaining > TimeSpan.Zero
                    ? remaining
                    : TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Remaining time between automatic start and automatic pause.
        /// Returns TimeSpan.Zero if schedule is disabled or dates are not configured.
        /// </summary>
        public TimeSpan ScheduleDuration
        {
            get
            {
                if (!UseSchedule ||
                    AutoStartDateTime == DateTime.MinValue ||
                    AutoPauseDateTime == DateTime.MinValue)
                    return TimeSpan.Zero;

                TimeSpan duration = AutoPauseDateTime - AutoStartDateTime;

                return duration > TimeSpan.Zero
                    ? duration
                    : TimeSpan.Zero;
            }
        }

        //----------------------------
        public bool RetryOnError { get; set; }

        public int MaxRetry { get; set; }
        public int RetryDelay_sec { get; set; }

        //------------------------------------------------------------------------------------
        /// <summary> If true, the download will be skipped if the file already exists at the destination path. If false, the existing file will be overwritten.</summary>
        public bool skipIfFileExists { get; set; }

        /// <summary>If true, the existing file will be overwritten if it already exists at the destination path. If false, the download will be Maked with Another Name.</summary>
        public bool overwriteIfFileExists { get; set; }

        private bool addToRearOfQueueAfterFailur { get; set; }

        //--
        /// <summary> If true, the download will be added to the rear of the queue after a failure. If false, the download will be Failed. </summary>
        public bool AddToRearOfQueueAfterFailure { get => FailCount >= MaxFailCount ? false : addToRearOfQueueAfterFailur ? true : false; set => addToRearOfQueueAfterFailur = value; }

        private bool removeFromQueueAfterFailure { get; set; }

        /// <summary> If true, the download will be removed from the queue after a failure. If false, the download will be added to the rear of the queue after a failure or the downlod will be Failed. </summary>
        public bool RemoveFromQueueAfterFailure { get => AddToRearOfQueueAfterFailure ? false : removeFromQueueAfterFailure ? true : false; set => removeFromQueueAfterFailure = value; }

        public int FailCount { get; set; }
        public int MaxFailCount { get; set; }
        //--

        //-------------------------------------------------------------------------------------
        // تنظیماتِ حافظه/دیسک
        public bool MinimizeDiskIO { get; set; } // هنوز ست نشده باید بعدا ست شه !
    }
}