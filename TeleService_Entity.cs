using System.Data;
using TL;

namespace TeleVault
{
    public sealed class TeleMediaInfo
    {
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

        public eTeleMediaDownloadStatus Status { get; set; } = eTeleMediaDownloadStatus.NotStarted;

        public long DownloadedBytes { get; set; }
        public string DownloadProgress => Media.Size > 0 ? $"{(DownloadedBytes * 100.0 / Media.Size):F2}%" : "0%";
        public string DestinationPath { get; init; } = string.Empty;
        public string TempFilePath { get; init; } = string.Empty;
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

        // تنظیماتِ حافظه/دیسک
        public bool MinimizeDiskIO { get; set; }
    }
}