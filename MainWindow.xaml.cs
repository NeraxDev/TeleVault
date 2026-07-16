using NeraXTools.LogManager;
using NeraXTools.TaskManager;
using System.IO;
using System.Windows;
using TL;
using WTelegram;

namespace TeleVault
{

    //====================================== Enums and Structs ===========================
    public enum eTeleMediaType { Photo, Document }
    public enum eTeleMediaFilter { All, Photo, Document }
    public enum eTeleMediaDownloadStatus { NotStarted, InProgress, Completed, Failed , Error }
    public enum eTelePerrType { User, Chat, Channel }
    public enum eMessageDirection{ NewestToOldest, OldestToNewest}
    public enum eDownloadPriority { High = 1, Medium = 2, Low = 3 }
    public enum eDownloadOpportunity { isMultiThreaded , isSingleThreaded  , isRetryOnError , isRetryOnFailure, isTryAgainAfterError , isWatingForNetWork , isUseDelayAfterError  }
    public class TeleMediaInfo
    {
        public long Id { get; set; }
        public long AccessHash { get; set; }
        public byte[] FileReference { get; set; }
        public long Size { get; set; }
        public int DcId { get; set; }
        public eTeleMediaType MediaType { get; set; }
        public InputFileLocationBase Location { get; set; }
    }
    public sealed class TeleDownloadTask 
    {
        public required TeleMediaInfo Media { get; init; }

        public required eTeleMediaDownloadStatus Status { get; set; }

        public long DownloadedBytes { get; set; }
        public string DownloadProgress => Media.Size > 0 ? $"{(DownloadedBytes * 100.0 / Media.Size):F2}%" : "0%";
        // مسیر نهایی فایل
        public required string DestinationPath { get; init; }
        // مسیر فایل موقت (.part / .tmp)
        public required string TempFilePath { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public List<TeleDownloadChunk> Chunks { get; } = new List<TeleDownloadChunk>();
    }
    public sealed class TeleDownloadChunk
    {
        public long StartOffset { get; init; }
        public long EndOffset { get; init; }
        public long DownloadedBytes { get; set; }
        public string DownloadProgress => (DownloadedBytes * 100.0 / (EndOffset - StartOffset + 1)).ToString("F2") + "%";
        public eTeleMediaDownloadStatus Status { get; set; }
    }
    //======================================== Main class ================================
    public partial class MainWindow : Window
    {
        //============================================== Global Fields and Properties ===========================
        // ======================= Main Method 
        public MainWindow()
        {
            InitializeComponent();
        }
        TeleService teleService = new TeleService(000, "YourApiHash", "YourAppName"); // Replace with your actual API ID, API Hash, and App Name
    }
    //======================================== TeleService Class ================================
    public class TeleService
    {
        private Client client;
        //============================================ Global Setting For Download
        private PriorityQueue<TeleMediaInfo, int> downloadQueue;
        private object? queueLock;
        private SemaphoreSlim _downloadSemaphore = new SemaphoreSlim(3);
        private int _maxMultiThreadedDownloads = 8; // تعداد دانلودهای همزمان
        private int _maxRetryCount = 3; // تعداد دفعات تلاش مجدد
        private int _delayBetweenRetriesMs = 5000; // ۵ ثانیه صبر بین هر تلاش
        private string DownloadDirectory { get; set; } = "Downloads";
        private eTeleMediaDownloadStatus GlobalState { get; set; } = eTeleMediaDownloadStatus.NotStarted;

        //--------------------------------------------
        //========================================Initialization ================================
        /// <summary>
        /// Initializes a new instance of the TeleService class with the specified API ID, API hash, and name, creating a WTelegram client for interacting with the Telegram API.
        /// </summary>
        /// <param name="apiId">The API ID for the Telegram application</param>
        /// <param name="apiHash">The API hash for the Telegram application</param>
        /// <param name="name">The name of the Telegram application</param>
        public TeleService(int apiId , string apiHash , string name)
        {
            client = new Client(apiId, apiHash , name);
        }
        //--------------------------------------------
        //======================================== Media Retrieval Methods ================================
        /// <summary>
        /// Retrieves media information from a Telegram message URL, extracting the username and message ID, and then fetching the associated media based on the specified type.
        /// </summary>
        /// <param name="url">The URL of the Telegram message</param>
        /// <param name="type">The type of the Telegram peer (Channel, Chat, or User)</param>
        /// <returns>A list of media information</returns>
        public async Task<List<TeleMediaInfo>> GetMediasByLink(string url, eTelePerrType type)
        {
            var data = await ParseUrl(url);
            return await GetMediasByMsgId(data.username, data.messageId, type, data.onlyDownloadFirst);
        }
        /// <summary>
        /// Retrieves media information from a specific Telegram message ID within a channel, chat, or user context.
        /// </summary>
        /// <param name="channelUsername">The username of the Telegram channel, chat, or user</param>
        /// <param name="messageId">The ID of the message to fetch media from</param>
        /// <param name="type">The type of the Telegram peer (Channel, Chat, or User)</param>
        /// <param name="onlyDownloadFirst">Indicates whether to download only the first media item</param>
        /// <returns>A list of media information</returns>
        public async Task<List<TeleMediaInfo>> GetMediasByMsgId(string channelUsername, int messageId, eTelePerrType type, bool onlyDownloadFirst)
        {

            var resultList = new List<TeleMediaInfo>();
            var resolved = await client.Contacts_ResolveUsername(channelUsername);
            var peerInfo = resolved.UserOrChat;

            // استفاده از نوع پایه تلگرام به جای object برای تایپ‌سیفتی بهتر
            Messages_MessagesBase messagesBase = null;

            switch (type)
            {
                case eTelePerrType.Channel:
                    if (peerInfo is Channel channel)
                    {
                        var inputChannel = new InputChannel(channel.id, channel.access_hash);
                        messagesBase = await client.Channels_GetMessages(inputChannel, messageId);
                    }
                    break;

                case eTelePerrType.Chat:
                case eTelePerrType.User:
                    InputPeer peer = peerInfo.ToInputPeer();
                    messagesBase = await client.Messages_GetHistory(peer, min_id: messageId - 1, max_id: messageId + 1, limit: 1);
                    break;
            }

            if (messagesBase == null) return resultList;

            MessageBase[] messageList = messagesBase.Messages;
            MessageBase[] targetMessages = onlyDownloadFirst ? messageList.Take(1).ToArray() : messageList;


            foreach (MessageBase msgBase in targetMessages)
            {
                if (msgBase is Message msg)
                {
                    var mediaInfo = ExtractMediaInfo(msg);
                    if (mediaInfo != null) resultList.Add(mediaInfo);
                }
            }
            return resultList;
        }
        /// <summary>
        /// Retrieves all media information from a Telegram channel, chat, or user based on the specified username, media type, filter, and message direction. It fetches messages in batches to avoid hitting Telegram's flood limits.
        /// </summary>
        /// <param name="channelUsername">The username of the Telegram channel, chat, or user</param>
        /// <param name="type">The type of the Telegram peer (Channel, Chat, or User)</param>
        /// <param name="filter">The filter for the type of media to retrieve</param>
        /// <param name="direction">The direction of the message history to fetch</param>
        /// <param name="checkPerRound">The number of messages to fetch per round</param>
        /// <returns>A list of all media information</returns>
        public async Task<List<TeleMediaInfo>> GetAllMediasByLink(string channelUsername, eTelePerrType type, eTeleMediaFilter filter = eTeleMediaFilter.All, eMessageDirection direction = eMessageDirection.NewestToOldest, int checkPerRound = 90)
        {
            var data = await ParseUrl(channelUsername);
            return await GetAllMediasByChanalName(data.username, type, filter, direction, checkPerRound);
        }
        /// <summary>
        /// Retrieves all media information from a Telegram channel, chat, or user based on the specified username, media type, filter, and message direction. It fetches messages in batches to avoid hitting Telegram's flood limits.
        /// </summary>
        /// <param name="channelUsername">The username of the Telegram channel, chat, or user</param>
        /// <param name="type">The type of the Telegram peer (Channel, Chat, or User)</param>
        /// <param name="filter">The filter for the type of media to retrieve</param>
        /// <param name="direction">The direction of the message history to fetch</param>
        /// <param name="checkPerRound">The number of messages to fetch per round</param>
        /// <returns>A list of all media information</returns>
        public async Task<List<TeleMediaInfo>> GetAllMediasByChanalName(string channelUsername, eTelePerrType type, eTeleMediaFilter filter = eTeleMediaFilter.All , eMessageDirection direction = eMessageDirection.NewestToOldest, int checkPerRound = 90)
        {
            List<TeleMediaInfo> allMedias = new List<TeleMediaInfo>();
            Contacts_ResolvedPeer resolved = await client.Contacts_ResolveUsername(channelUsername);
            InputPeer peer = resolved.UserOrChat.ToInputPeer();

            MessagesFilter telegramFilter = filter switch
            {
                eTeleMediaFilter.Photo => new InputMessagesFilterPhotos(),
                eTeleMediaFilter.Document => new InputMessagesFilterDocument(),
                _ => null // All
            };
            int min_id = 0;
            int offset_id = 0; 
            while (true)
            {
                // 90 Msg Per request is a safe limit to avoid hitting Telegram's flood limits
                Messages_MessagesBase messagesBase = await client.Messages_GetHistory(peer, offset_id: offset_id,min_id: min_id, limit: checkPerRound);

                if (messagesBase.Messages.Length == 0) break;

                foreach (MessageBase msgBase in messagesBase.Messages)
                {
                    if (msgBase is Message msg && msg.media != null && (filter == eTeleMediaFilter.All || (filter == eTeleMediaFilter.Photo && msg.media is MessageMediaPhoto) || (filter == eTeleMediaFilter.Document && msg.media is MessageMediaDocument)))
                    {
                        allMedias.Add(ExtractMediaInfo(msg));
                    }
                }
                offset_id = direction == eMessageDirection.NewestToOldest ? messagesBase.Messages.Last().ID : 0;
                min_id = direction == eMessageDirection.OldestToNewest ? messagesBase.Messages.Last().ID : 0;
                await Task.Delay(500); // Delay to avoid hitting Telegram's flood limits
            }

            return allMedias;
        }
        //------------------------------------------------
        //================================================ URL Parsing Method ================================
        /// <summary>
        /// Parses a Telegram message URL and extracts the username, message ID, and whether to only download the first media.
        /// </summary>
        /// <param name="url">The URL of the Telegram message</param>
        /// <returns>A tuple containing the username, message ID, and download flag</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="FormatException"></exception>
        public static async Task<(string username, int messageId, bool onlyDownloadFirst)> ParseUrl(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                    throw new ArgumentException("URL cannot be null or empty.", nameof(url));

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    throw new ArgumentException("Invalid URL.", nameof(url));

                // آیا ?single وجود دارد؟
                bool onlyDownloadFirst = string.Equals(uri.Query.TrimStart('?'), "single", StringComparison.OrdinalIgnoreCase);

                // /plugins3d/2138
                string[] parts = uri.AbsolutePath.Trim('/').Split('/');

                if (parts.Length != 2)
                    throw new FormatException("Invalid Telegram message URL.");

                string username = parts[0];

                if (!int.TryParse(parts[1], out int messageId))
                    throw new FormatException("Invalid message id.");

                return (username, messageId, onlyDownloadFirst);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
                return (string.Empty, 0, false);
            }
        }
        //-------------------------------------------------
        #region =============================================== Download Media Methods ================================
        //========================================================================
        // متدهای سربارگذاری شده (Overloads)
        //========================================================================

        public void AddToQueue(TeleMediaInfo media, int priority = 1 , bool autoStart = false)
            => AddToQueue_Core(new List<TeleMediaInfo> { media }, priority, autoStart);
        public void AddToQueue(List<TeleMediaInfo> media, int priority = 1, bool autoStart = false)
            => AddToQueue_Core(media, priority, autoStart);
        public void AddToQueue(params TeleMediaInfo[] media)
            => AddToQueue_Core(media.ToList(), 1, false);
        public void AddToQueue(TeleMediaInfo[] media, int priority = 1, bool autoStart = false)
            => AddToQueue_Core(media.ToList(), priority, autoStart);
        public void AddToQueue(TeleMediaInfo media, eDownloadPriority priority, bool autoStart = false)
            => AddToQueue_Core(new List<TeleMediaInfo> { media }, (int)priority, autoStart);
        public void AddToQueue(TeleMediaInfo[] media, eDownloadPriority priority, bool autoStart = false)
            => AddToQueue_Core(media.ToList(), (int)priority, autoStart);
        public void SetMaxConcurrentDownloads(int count)
        {
            _downloadSemaphore.Dispose();
            _downloadSemaphore = new SemaphoreSlim(count);
        }


 
        //========================================================================
        // هسته مرکزی (Core Logic)
        //========================================================================
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

                // ۳. بررسی اتمام
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

        private async Task DownloadSingleChunkAsync(TeleDownloadTask task, TeleDownloadChunk chunk, CancellationToken ct)
        {
            long currentOffset = chunk.StartOffset + chunk.DownloadedBytes;
            using (var fs = new FileStream(task.TempFilePath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                while (currentOffset <= chunk.EndOffset)
                {
                    int limit = 1024 * 1024; // 1MB per request
                    if (currentOffset + limit > chunk.EndOffset)
                        limit = (int)(chunk.EndOffset - currentOffset + 1);

                    // فراخوانی مستقیم متد سطح پایین با افست دقیق
                    var result = await client.Upload_GetFile(task.Media.Location, currentOffset, limit);

                    if (result is Upload_File fileResult)
                    {
                        fs.Position = currentOffset;
                        await fs.WriteAsync(fileResult.bytes, 0, fileResult.bytes.Length, ct);

                        currentOffset += fileResult.bytes.Length;
                        chunk.DownloadedBytes += fileResult.bytes.Length;
                        task.DownloadedBytes = task.Chunks.Sum(c => c.DownloadedBytes); // آپدیت کل
                    }
                    else
                    {
                        throw new Exception("Download failed: Unexpected response type.");
                    }
                }
            }
            chunk.Status = eTeleMediaDownloadStatus.Completed;
        }
        #endregion
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