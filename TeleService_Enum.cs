namespace TeleVault
{
    public enum eTeleMediaType
    { Photo, Document }

    public enum eTeleMediaFilter
    { All, Photo, Document }

    public enum eTeleMediaDownloadStatus
    { NotStarted, InProgress, Completed, Failed, Error, Watching, Paused, Finalizing }

    public enum eTelePerrType
    { User, Chat, Channel }

    public enum eMessageDirection
    { NewestToOldest, OldestToNewest }

    public enum eDownloadPriority
    { High = 1, Medium = 2, Low = 3 }

    public enum eDownloadChunkSize
    {
        KB_128 = 1,    // 128 KB
        KB_256 = 2,    // 256 KB
        KB_384 = 3,    // 384 KB
        KB_512 = 4,    // 512 KB
        KB_640 = 5,    // 640 KB
        KB_768 = 6,    // 768 KB (0.75 MB)
        KB_896 = 7,    // 896 KB
        MB_1 = 8,     // 1 MB
        MB_2 = 16,    // 2 MB
        MB_3 = 24,    // 3 MB
        MB_4 = 32,    // 4 MB
        MB_5 = 40,    // 5 MB
        MB_6 = 48,    // 6 MB
        MB_7 = 56,    // 7 MB
        MB_8 = 64,    // 8 MB
        MB_9 = 72,    // 9 MB
        MB_10 = 80,    // 10 MB
        MB_11 = 88,    // 11 MB
        MB_12 = 96,    // 12 MB
        MB_13 = 104,   // 13 MB
        MB_14 = 112,   // 14 MB
        MB_15 = 120,   // 15 MB
        MB_16 = 128,   // 16 MB
        MB_17 = 136,   // 17 MB
        MB_18 = 144,   // 18 MB
        MB_19 = 152,   // 19 MB
        MB_20 = 160    // 20 MB
    }
}