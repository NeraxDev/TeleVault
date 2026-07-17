using NeraXTools.LogManager;
using System.IO;
using System.Net;
using System.Windows;
using TL;
using WTelegram;

namespace TeleVault
{
    public sealed partial class TeleService
    {
        private Client client;
        private DownloadGlobalSettingsDTO globalDownloadPolicy;

        //============================================ Global Setting For Download
        private PriorityQueue<TeleMediaInfo, int> downloadQueue;

        private object? queueLock;

        private int _maxMultiThreadedDownloads = 8; // تعداد دانلودهای همزمان
        private int globalMaxRetryCount = 3; // تعداد دفعات تلاش مجدد
        private int globalDelayBetweenRetriesMs = 5000; // ۵ ثانیه صبر بین هر تلاش
        private eDownloadChunkSize _currentChunkSize = eDownloadChunkSize.MB_1; // Default chunk size for downloads 1 MB
        private string DownloadDirectory { get; set; } = "Downloads";
        private eTeleMediaDownloadStatus GlobalState { get; set; } = eTeleMediaDownloadStatus.NotStarted;

        //========================================Initialization ================================
        /// <summary>
        /// Initializes a new instance of the TeleService class with the specified API ID, API hash, and name, creating a WTelegram client for interacting with the Telegram API.
        /// </summary>
        /// <param name="apiId">The API ID for the Telegram application</param>
        /// <param name="apiHash">The API hash for the Telegram application</param>
        /// <param name="name">The name of the Telegram application</param>
        private void AddToQueue_Core(List<TeleMediaInfo> media, int priority, bool autoStart)
        {
            if (downloadQueue == null) downloadQueue = new PriorityQueue<TeleMediaInfo, int>();
            if (queueLock == null) queueLock = new object();

            lock (queueLock)
                foreach (var item in media)
                    downloadQueue.Enqueue(item, priority);

            if (autoStart && GlobalState != eTeleMediaDownloadStatus.InProgress)
                throw null;
            //_ = ProcessQueue_Core();
        }

        private async Task InitializeChunks(TeleDownloadTask task)
        {
            if (task.Chunks.Any()) return;
            if (globalDownloadPolicy == null)
                globalDownloadPolicy = new DownloadGlobalSettingsDTO();
            if (task.TempFilePath == null)
                task.TempFilePath = Path.Combine(GlobalTempPath, $"{task.Media.Id}_{Guid.NewGuid()}.tmp");
            ApplyGlobalSetting(task, globalDownloadPolicy);
            task.policy.UseMultiThreaded = task.Media.Size > 10 * 1024 * 1024; // اگر فایل بزرگتر از ۱۰ مگابایت بود، دانلود چند تکه‌ای فعال شود
            long chunkSize = task.policy.UseMultiThreaded ? task.Media.Size / _maxMultiThreadedDownloads : task.Media.Size;
            for (int i = 0; i < _maxMultiThreadedDownloads; i++)
            {
                long start = i * chunkSize;
                long end = (i == _maxMultiThreadedDownloads - 1) ? task.Media.Size - 1 : (start + chunkSize - 1);
                task.Chunks.Add(new TeleDownloadChunk { StartOffset = start, EndOffset = end, Status = eTeleMediaDownloadStatus.NotStarted });
            }
        }

        //private async Task DownloadMediaManager_Core(TeleDownloadTask task, CancellationToken ct, eDownloadOpportunity[] opportunities)
        //{
        //    try
        //    {
        //        await DownloadMedia_Core(task, ct, opportunities);
        //    }
        //    catch (Exception ex)
        //    {
        //        task.Status = eTeleMediaDownloadStatus.Failed;
        //        // Log the exception or handle it as needed
        //    }
        //}
        private async Task DownloadMedia_Core(TeleDownloadTask task, CancellationToken ct)
        {
            await _downloadSemaphore.WaitAsync(ct);
            do
            {
                while (!await IsInternetConnected(10, ct))
                    if (task.policy.WaitForNetwork && task.policy.currentRetryCountForNetwork < task.policy.waitForNetworkRetryCount)
                    {
                        task.Status = eTeleMediaDownloadStatus.Watching;
                        task.policy.currentRetryCountForNetwork++;
                        await Task.Delay(task.policy.RetryDelay_sec * 1000, ct);
                    }
                    else
                    {
                        if (task.Status == eTeleMediaDownloadStatus.InProgress && task.DownloadedBytes > 10)
                            task.Status = eTeleMediaDownloadStatus.Paused;
                        else
                            task.Status = eTeleMediaDownloadStatus.Failed;
                        return;
                    }

                try
                {
                    await InitializeChunks(task);
                    task.Status = eTeleMediaDownloadStatus.InProgress;

                    var chunkTasks = task.Chunks.Select(chunk =>
                        DownloadSingleChunkAsync(task, chunk, ct)
                    );

                    await Task.WhenAll(chunkTasks);

                    if (task.Chunks.All(c => c.Status == eTeleMediaDownloadStatus.Completed))
                    {
                        File.Move(task.TempFilePath, task.DestinationPath); // TODO : باید با ابزار های ایسنک نیراکس تول جایگزین شود
                        task.Status = eTeleMediaDownloadStatus.Completed;
                    }
                    else
                        throw new Exception("Not all chunks completed successfully.");
                }
                catch (RpcException ex)
                {
                    // این یعنی تلگرام جواب داده، ولی یک مشکلی هست (مثلا فایل پیدا نشد، یا لیمیت خوردی)
                    // این‌ها «قطعی اینترنت» نیستند!
                    // مستقیم ارور ست میکنیم اینجا چون ارور واقعا از سمت تلگرام هست و نه اینترنت
                    task.Status = eTeleMediaDownloadStatus.Error;
                }
                catch (System.IO.IOException ex)
                {
                    // این معمولاً مربوط به قطع شدنِ سوکت است (Network Issue)
                    task.Status = eTeleMediaDownloadStatus.Error;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    // این قطعاً قطعی اینترنت یا فیلترینگ است
                }
                catch (Exception ex) when (ex.Message.Contains("No such host") || ex.Message.Contains("Name resolution failure"))
                {
                    // DNS مشکل دارد یا اینترنت کاملاً قطع است
                }
                finally
                {
                    if (task.Status == eTeleMediaDownloadStatus.Paused || task.Status == eTeleMediaDownloadStatus.Failed || task.Status == eTeleMediaDownloadStatus.Completed)
                        _downloadSemaphore.Release();
                }
            }
            while (task.policy.RetryOnError && task.Status == eTeleMediaDownloadStatus.Error);
        }

        private async Task DownloadSingleChunkAsync(TeleDownloadTask task, TeleDownloadChunk chunk, CancellationToken ct)
        {
            if (chunk.Status == eTeleMediaDownloadStatus.Completed || task.Status == eTeleMediaDownloadStatus.Completed) return;
            task.Status = chunk.Status = eTeleMediaDownloadStatus.InProgress;
            long currentOffset = chunk.StartOffset + chunk.DownloadedBytes;
            using (var fs = new FileStream(task.TempFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                while (currentOffset <= chunk.EndOffset)
                {
                    int limit = GetLimitInBytes();
                    if (currentOffset + limit > chunk.EndOffset)
                        limit = (int)(chunk.EndOffset - currentOffset + 1);
                    var result = await client.Upload_GetFile(task.Media.Location, currentOffset, limit);

                    if (result is Upload_File fileResult)
                    {
                        lock (fs)
                        {
                            fs.Position = currentOffset;
                            fs.Write(fileResult.bytes, 0, fileResult.bytes.Length);
                        }

                        currentOffset += fileResult.bytes.Length;
                        chunk.DownloadedBytes += fileResult.bytes.Length;
                        task.DownloadedBytes = task.Chunks.Sum(c => c.DownloadedBytes);
                    }
                }
            }
            chunk.Status = eTeleMediaDownloadStatus.Completed;
        }

        private int GetLimitInBytes() => (int)_currentChunkSize * 1024 * 128;

        //=================================================== Internal Methods ==========================================
        /// <summary>
        /// Extracts media information from a Telegram message, returning a TeleMediaInfo object containing details about the media, such as ID, access hash, file reference, size, data center ID, media type, and location.
        /// </summary>
        /// <param name="msg">The Telegram message from which to extract media information</param>
        /// <returns>A TeleMediaInfo object containing the extracted media information</returns>
        private TeleMediaInfo ExtractMediaInfo(Message msg)
        {
            TeleMediaInfo resultList = null;
            if (msg.media == null)
            {
                MessageBox.Show("No media found in the message."); // Convert To Internal Log Manager After Transfar into NeraXTools Library
                return null;
            }
            if (msg.media is MessageMediaDocument docMedia && docMedia.document is Document doc)
            {
                resultList = new TeleMediaInfo
                {
                    Id = doc.id,
                    AccessHash = doc.access_hash,
                    FileReference = doc.file_reference,
                    Size = doc.size,
                    DcId = doc.dc_id,
                    MediaType = eTeleMediaType.Document,
                    Location = new InputDocumentFileLocation { id = doc.id, access_hash = doc.access_hash, file_reference = doc.file_reference, thumb_size = null }
                };
            }
            else if (msg.media is MessageMediaPhoto photoMedia && photoMedia.photo is Photo photo)
            {
                PhotoSize bestSize = photo.sizes.OfType<PhotoSize>().Last();
                resultList = new TeleMediaInfo
                {
                    Id = photo.id,
                    AccessHash = photo.access_hash,
                    FileReference = photo.file_reference,
                    Size = bestSize.size,
                    DcId = photo.dc_id,
                    MediaType = eTeleMediaType.Photo,
                    Location = new InputPhotoFileLocation { id = photo.id, access_hash = photo.access_hash, file_reference = photo.file_reference, thumb_size = null }
                };
            }
            return resultList;
        }

        private async Task<bool> IsInternetConnected(int timeout, CancellationToken ct)
        {
            try
            {
                using (var client = new System.Net.NetworkInformation.Ping())
                {
                    var reply = await client.SendPingAsync(address: IPAddress.Parse("8.8.8.8"), timeout: TimeSpan.FromSeconds(timeout), cancellationToken: ct);
                    return reply?.Status == System.Net.NetworkInformation.IPStatus.Success;
                }
            }
            catch { return false; }
        }

        private DateTime CreateDateTime(int year, int month, int day, int hour, int minute)
        {
            DateTime now = DateTime.Now;
            year = year == -1 ? now.Year : year;
            month = month == -1 ? now.Month : month;
            day = day == -1 ? now.Day : day;
            hour = hour == -1 ? now.Hour : hour;
            minute = minute == -1 ? now.Minute : minute;

            return new DateTime(year, month, day, hour, minute, 0);
        }

        private void ApplyGlobalSetting(TeleDownloadTask task, DownloadGlobalSettingsDTO global)
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

        //--------------------------------------------------
    }
}