using WTelegram;
using System.Windows;
using TL;

namespace TeleVault
{
    public partial class TeleService
    {
        //======================================== Initialization ================================
        /// <summary>
        /// Initializes a new instance of the TeleService class with the specified API ID, API hash, and phone number, creating a WTelegram client for interacting with the Telegram API.
        /// </summary>
        /// <param name="apiId">The API ID for the Telegram application</param>
        /// <param name="apiHash">The API hash for the Telegram application</param>
        /// <param name="phoneNumber">The phone number for the Telegram account</param>
        public TeleService(int apiId, string apiHash, string phoneNumber)
        {
            client = new Client(apiId, apiHash, phoneNumber);
        }

        //======================================== Media Retrieval Methods ================================
        /// <summary>
        /// Retrieves media information from a Telegram message URL, extracting the username and message ID, and then fetching the associated media based on the specified type.
        /// </summary>
        /// <param name="url">The URL of the Telegram message</param>
        /// <param name="type">The type of the Telegram peer (Channel, Chat, or User)</param>
        /// <returns>A list of media information</returns>
        public async Task<List<TeleMediaInfo>> GetMediasByLink(string url, eTelePerrType type)
        {
            var data = await TeleParseUrl(url);
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
            var data = await TeleParseUrl(channelUsername);
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
        public async Task<List<TeleMediaInfo>> GetAllMediasByChanalName(string channelUsername, eTelePerrType type, eTeleMediaFilter filter = eTeleMediaFilter.All, eMessageDirection direction = eMessageDirection.NewestToOldest, int checkPerRound = 90)
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
                Messages_MessagesBase messagesBase = await client.Messages_GetHistory(peer, offset_id: offset_id, min_id: min_id, limit: checkPerRound);

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

        //================================================ URL Parsing Method ================================
        /// <summary>
        /// Parses a Telegram message URL and extracts the username, message ID, and whether to only download the first media.
        /// </summary>
        /// <param name="url">The URL of the Telegram message</param>
        /// <returns>A tuple containing the username, message ID, and download flag</returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="FormatException"></exception>
        public async Task<(string username, int messageId, bool onlyDownloadFirst)> TeleParseUrl(string url)
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

        public void AddToQueue(TeleMediaInfo media, int priority = 1, bool autoStart = false)
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

        public void SetDownloadChunkSize(eDownloadChunkSize size)
          => _currentChunkSize = size;

        public void SetDefaultDownloadGlobalSettings()
        {
            globalDownloadSettings_In ??= new DownloadGlobalSettingsDTO();

            globalDownloadSettings_In.MaxThreads = 8;

            globalDownloadSettings_In.WaitForNetwork = true;

            globalDownloadSettings_In.WaitForNetworkTimeout_sec = 30;

            globalDownloadSettings_In.WaitForNetworkRetryCount = 5;

            globalDownloadSettings_In.RetryOnError = true;

            globalDownloadSettings_In.MaxRetry = 3;

            globalDownloadSettings_In.RetryDelay_sec = 5;

            globalDownloadSettings_In.MinimizeDiskIO = false;
        }

        #endregion =============================================== Download Media Methods ================================
    }
}