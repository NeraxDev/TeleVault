namespace TeleVault
{
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
        public required string DestinationPath { get; init; }
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
}
