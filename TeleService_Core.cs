using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using TeleVault;
using TL;
using WTelegram;

namespace TeleVault
{
    public sealed partial class TeleService
    {

        public Client client;
        //============================================ Global Setting For Download
        private string GlobalTempPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NeraX", "TeleVault", "Temp");
        private string GlobalDestinationPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "TeleVault");
        private PriorityQueue<TeleMediaInfo, int> downloadQueue;
        private object? queueLock;
        private SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3);
        private int _maxMultiThreadedDownloads = 8; // تعداد دانلودهای همزمان
        private int _maxRetryCount = 3; // تعداد دفعات تلاش مجدد
        private int _delayBetweenRetriesMs = 5000; // ۵ ثانیه صبر بین هر تلاش
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

            long chunkSize = task.Media.Size / _maxMultiThreadedDownloads;
            for (int i = 0; i < _maxMultiThreadedDownloads; i++)
            {
                long start = i * chunkSize;
                long end = (i == _maxMultiThreadedDownloads - 1) ? task.Media.Size - 1 : (start + chunkSize - 1);
                task.Chunks.Add(new TeleDownloadChunk { StartOffset = start, EndOffset = end, Status = eTeleMediaDownloadStatus.NotStarted });
            }
        }
        private async Task DownloadMedia_Core(TeleDownloadTask task, CancellationToken ct, eDownloadOpportunity[] opportunities)
        {
            await _downloadSemaphore.WaitAsync(ct);
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
                    File.Move(task.TempFilePath, task.DestinationPath);
                    task.Status = eTeleMediaDownloadStatus.Completed;
                }
            }
            finally
            {
                _downloadSemaphore.Release(); // آزاد کردنِ سهمیه فایل
            }
        }
        private int GetLimitInBytes() => (int)_currentChunkSize * 1024 * 128;
        private async Task DownloadSingleChunkAsync(TeleDownloadTask task, TeleDownloadChunk chunk, CancellationToken ct)
        {
            long currentOffset = chunk.StartOffset + chunk.DownloadedBytes;
            using (var fs = new FileStream(task.TempFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                while (currentOffset <= chunk.EndOffset)
                {
                    try
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
                    catch (Exception ex)
                    {
                        await Task.Delay(_delayBetweenRetriesMs, ct);
                    }
                }
            }
            chunk.Status = eTeleMediaDownloadStatus.Completed;
        }
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
        //--------------------------------------------------

    }
}