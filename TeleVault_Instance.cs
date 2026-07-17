using WTelegram;

namespace TeleVault
{
    public sealed partial class TeleService
    {
        private Client client;  // WTelegram client instance for interacting with the Telegram API
        private DownloadGlobalSettingsDTO globalDownloadSettings_In;
    }
}