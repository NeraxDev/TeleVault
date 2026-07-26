using NeraXTools;
using NeraXTools.LogManager;
using NeraXTools.TaskManager;
using System.IO;
using System.Linq.Expressions;
using System.Net;
using System.Windows;
using TL;
using WTelegram;

namespace TeleVault
{
    public sealed partial class TeleService
    {
        //============================================ Global Setting For Download
        private PriorityQueue<TeleMediaInfo, int> downloadQueue;

        private object? queueLock;
        private eTeleMediaDownloadStatus GlobalDownloadState { get; set; } = eTeleMediaDownloadStatus.NotStarted; //

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
            if (autoStart && GlobalDownloadState != eTeleMediaDownloadStatus.InProgress) // Check if the download is already in progress to avoid starting it again
            {
                GlobalDownloadState = eTeleMediaDownloadStatus.InProgress;
                //_ = ProcessQueue_Core(); // Start processing the queue asynchronously without awaiting it
            }
        }

        /// <summary>
        /// English: Initializes the download chunks for a given TeleDownloadTask based on the media size and download policy, creating chunk objects with start and end offsets for multi-threaded downloading.
        /// Persian: بخش‌های دانلود را برای یک TeleDownloadTask مشخص بر اساس اندازه رسانه و سیاست دانلود مقداردهی اولیه می‌کند و اشیاء chunk با آفست‌های شروع و پایان برای دانلود چند رشته‌ای ایجاد می‌کند.
        /// </summary>
        /// <param name="task">The TeleDownloadTask for which to initialize chunks.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task InitializeChunks(TeleDownloadTask task)
        {
            if (task.Chunks.Any()) return;
            if (globalDownloadSettings_In == null)
                globalDownloadSettings_In = new DownloadGlobalSettingsDTO();

            task.FileName = task.Media.FileName;
            task.FileExtension = Path.GetExtension(task.Media.FileName);

            await ApplyDownlodGlobalSetting(task, globalDownloadSettings_In);

            if (task.FullTempFilePath == null)
            {
                Logger.log("FullTempFilePath is null.", eLogType.Error, eLogRecordMode.UI);
                throw new Exception("FullTempFilePath is null.");
            }

            long chunkSize = task.policy.UseMultiThreaded ? task.Media.Size / task.policy.MaxThreads : task.Media.Size;
            for (int i = 0; i < task.policy.MaxThreads; i++)
            {
                long start = i * chunkSize;
                long end = (i == task.policy.MaxThreads - 1) ? task.Media.Size - 1 : (start + chunkSize - 1);
                if (!task.AddChunk(start, end))
                {
                    task.ClearChunks(); // Clear any chunks that were added before the failure For try again in next round
                    throw new Exception($" Can't Add Chunk Downlod ID -> {task.Media.Id}");
                }
            }
        }

        /// <summary>
        /// English: Core method for downloading media in a TeleDownloadTask, handling retries, network checks, and chunked downloading. It manages the download process, including error handling, pausing, and finalizing the download.
        /// Persian: روش هسته‌ای برای دانلود رسانه در یک TeleDownloadTask, که مدیریت بازخوانی‌ها, بررسی شبکه و دانلود با قطعات را انجام می‌دهد. این روش فرآیند دانلود را مدیریت می‌کند, از جمله مدیریت خطا, توقف و پایان‌دادن دانلود.
        /// </summary>
        /// <param name="task">The TeleDownloadTask to download.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task DownloadMedia_Core(TeleDownloadTask task, CancellationToken ct)
        {
            try
            {
                await globalDownloadSettings_In.GetDownloadSemaphore.WaitAsync(ct);
                int curentErrorRetryCount = 0;
                do
                {
                    if (task.Status == eTeleMediaDownloadStatus.Paused)
                    {
                        Logger.log("Downlod Paused ", eLogType.Info, eLogRecordMode.UI);
                        break;
                    }
                    if (task.Status == eTeleMediaDownloadStatus.Cancelled)
                    {
                        task.Chunks.Select(c => c.Status = eTeleMediaDownloadStatus.Cancelled).ToList();
                        task.ClearChunks();
                        File.Delete(task.FullTempFilePath); // TODO : باید با ابزار های ایسنک نیراکس تول جایگزین شود
                        task.DownloadedBytes = 0; // TODO : وقتی دیلیت فایل نیراکس شد این معکوس باید کم بشه مقدارش تا صفر شه و کنسل محسوب شه !
                        Logger.log("Download Cancelled", eLogType.Info, eLogRecordMode.UI);
                    }
                    if (task.Status == eTeleMediaDownloadStatus.Error)
                    {
                        if (curentErrorRetryCount >= task.policy.MaxRetry)
                            break;
                        curentErrorRetryCount++;
                    }
                    while (!await IsInternetConnected(10, ct))
                        if (task.policy.WaitForNetwork && task.policy.currentRetryCountForNetwork < task.policy.waitForNetworkRetryCount)
                        {
                            task.Status = eTeleMediaDownloadStatus.Watching;
                            task.policy.currentRetryCountForNetwork++;
                            await Task.Delay(task.policy.waitForNetworkDelay_sec * 1000, ct);
                        }
                        else
                        {
                            if (task.Status == eTeleMediaDownloadStatus.InProgress && task.DownloadedBytes > 10)
                            {
                                task.Status = eTeleMediaDownloadStatus.Paused;
                                task.Chunks.Select(c => c.Status = eTeleMediaDownloadStatus.Paused).ToList();
                            }
                            else
                            {
                                task.Status = eTeleMediaDownloadStatus.Failed;
                                task.Chunks.Select(c => c.Status = eTeleMediaDownloadStatus.Failed).ToList();
                            }
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
                            _ = TaskSchedulerEngine.RunSyncAsAsync(() => File.Move(task.FullTempFilePath, task.FullPath), ePriorityLevel.MidLevel, ct); // TODO : باید با ابزار های ایسنک نیراکس تول جایگزین شود
                            task.Status = eTeleMediaDownloadStatus.Finalizing;
                            task.DownloadedBytes = 0;
                            task.isOnMoving = true;
                            FileInfo fileInfo = new FileInfo(task.FullPath);
                            (int, long) lastRoundAndRoundSize = (0, 0);
                            int roundCount = 0;
                            while (task.DownloadedBytes <= task.Media.Size && task.isOnMoving)
                            {
                                roundCount++;
                                task.DownloadedBytes = fileInfo.Length;
                                fileInfo.Refresh();
                                await Task.Delay(500);
                                //
                                if (lastRoundAndRoundSize.Item1 + 20 <= roundCount && lastRoundAndRoundSize.Item2 >= task.DownloadedBytes)
                                    throw new Exception("File move operation seems to be stuck.");
                                else
                                    lastRoundAndRoundSize = (roundCount, task.DownloadedBytes);
                            }
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
                        Logger.log($"Telegram RpcException \n\t Exception : {ex.Message}", eLogType.Exception, eLogRecordMode.UI);
                    }
                    catch (System.IO.IOException ex)
                    {
                        // این معمولاً مربوط به قطع شدنِ سوکت است (Network Issue)
                        task.Status = eTeleMediaDownloadStatus.Error;
                    }
                    catch (System.Net.Sockets.SocketException)
                    {
                        int rollbackSize = globalDownloadSettings_In.GetRollbackSize(task.policy.GetChunkSizeValue == null ? eDownloadChunkSize.MB_1 : (eDownloadChunkSize)(task.policy.GetChunkSizeValue / 1024 / 128));
                        foreach (var chunk in task.Chunks)
                        {
                            if (chunk.Status == eTeleMediaDownloadStatus.Error)
                            {
                                chunk.DownloadedBytes = Math.Max(0, chunk.DownloadedBytes - rollbackSize);
                            }
                        }
                    }
                    catch (Exception ex) when (ex.Message.Contains("No such host") || ex.Message.Contains("Name resolution failure"))
                    {
                        task.Status = eTeleMediaDownloadStatus.Error;
                        Logger.log($"Network/DNS issue detected! \n Exception Message : {ex.Message} \n Download ID -> {task.Media.Id}", eLogType.Exception, eLogRecordMode.UI);
                    }
                    catch (Exception ex)
                    {
                        task.Status = eTeleMediaDownloadStatus.Error;
                        Logger.log($"Unexpected error occurred \n Exception Message : {ex.Message} \n Download ID -> {task.Media.Id}", eLogType.Exception, eLogRecordMode.UI);
                    }
                    finally
                    {
                        if (task.Status == eTeleMediaDownloadStatus.Paused || task.Status == eTeleMediaDownloadStatus.Failed || task.Status == eTeleMediaDownloadStatus.Completed)
                            globalDownloadSettings_In.GetDownloadSemaphore.Release();
                        else if (task.Status == eTeleMediaDownloadStatus.Error)
                            await Task.Delay(task.policy.RetryDelay_sec * 1000, ct);
                    }
                }
                while (task.policy.RetryOnError && task.Status == eTeleMediaDownloadStatus.Error);

                if (task.Status != eTeleMediaDownloadStatus.Completed || task.Status != eTeleMediaDownloadStatus.Paused || task.Status != eTeleMediaDownloadStatus.Finalizing)
                    throw new Exception("Download failed after maximum retries.");
            }
            catch (Exception ex)
            {
                task.Status = eTeleMediaDownloadStatus.Failed;
                task.Chunks.Select(c => c.Status = eTeleMediaDownloadStatus.Failed).ToList();
                task.ClearChunks();
                File.Delete(task.FullTempFilePath); // TODO : باید با ابزار های ایسنک نیراکس تول جایگزین شود
                task.DownloadedBytes = 0; // TODO : وقتی دیلیت فایل نیراکس شد این معکوس باید کم بشه مقدارش تا صفر شه تا نمایش دهنده درجه حذف محسوب شه !
                Logger.log($"Download Failed ! \n \t File ID  : {task.Media.Id}", eLogType.Info, eLogRecordMode.UI);
            }
        }

        /// <summary>
        /// English: Downloads a single chunk of media for a given TeleDownloadTask, handling the download process, writing to the temporary file, and updating the chunk's status. It checks for cancellation and handles errors appropriately.
        /// </summary>
        /// <param name="task">The TeleDownloadTask for which to download the chunk.</param>
        /// <param name="chunk">The TeleDownloadChunk to download.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task DownloadSingleChunkAsync(TeleDownloadTask task, TeleDownloadChunk chunk, CancellationToken ct)
        {
            if (chunk.Status == eTeleMediaDownloadStatus.Completed || task.Status == eTeleMediaDownloadStatus.Completed) return; // If the chunk is already completed or the task is completed, skip downloading this chunk
            task.Status = chunk.Status = eTeleMediaDownloadStatus.InProgress;
            long currentOffset = chunk.StartOffset + chunk.DownloadedBytes;
            try
            {
                using (var fs = new FileStream(task.FullTempFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
                {
                    while (currentOffset <= chunk.EndOffset && chunk.Status != eTeleMediaDownloadStatus.Finalizing && chunk.Status != eTeleMediaDownloadStatus.Paused && chunk.Status != eTeleMediaDownloadStatus.Cancelled && !ct.IsCancellationRequested)
                    {
                        int limit = task.policy.GetChunkSizeValue;
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
            }
            finally { chunk.Status = eTeleMediaDownloadStatus.Error; } // if an exception occurs, mark the chunk as Error. This will allow the retry mechanism to handle it appropriately. // Dont Worry , if the chunk is completed, it will be set to Completed in the next line.

            chunk.Status = eTeleMediaDownloadStatus.Completed;
        }

        //=================================================== Helper Methods ==========================================
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
                    FileName = doc.attributes.OfType<DocumentAttributeFilename>().FirstOrDefault()?.file_name + doc.id ?? "unknown",
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
                    FileName = $"photo_{photo.id}",
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

        /// <summary>
        /// English: Creates a DateTime object based on the provided year, month, day, hour, and minute parameters. If any parameter is set to -1, it defaults to the current date and time for that component.
        /// </summary>
        /// <param name="year">The year to set, or -1 to use the current year.</param>
        /// <param name="month">The month to set, or -1 to use the current month.</param>
        /// <param name="day">The day to set, or -1 to use the current day.</param>
        /// <param name="hour">The hour to set, or -1 to use the current hour.</param>
        /// <param name="minute">The minute to set, or -1 to use the current minute.</param>
        /// <returns>The created DateTime object.</returns>
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

        /// <summary>
        /// English: Applies global download settings to a specific TeleDownloadTask, updating its policy and paths based on the provided DownloadGlobalSettingsDTO. It ensures that the task's settings are consistent with the global configuration.
        /// </summary>
        /// <param name="task">The TeleDownloadTask to which to apply the global settings.</param>
        /// <param name="global">The DownloadGlobalSettingsDTO containing the global settings.</param>
        /// <exception cref="ArgumentNullException"></exception>

        //--------------------------------------------------
    }
}