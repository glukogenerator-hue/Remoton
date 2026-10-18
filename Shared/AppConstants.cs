namespace Remotely.Shared;

public class AppConstants
{
    public const string DefaultProductName = "Remotely";
    public const string DefaultPublisherName = "Immense Networks";

    /// <summary>
    /// Адрес нашего сервера. Автономный клиент использует его, если адрес не встроен в exe
    /// и не передан в параметрах, чтобы пользователю не приходилось вводить его вручную.
    /// </summary>
    public const string DefaultServerUrl = "https://help.urldot.ru";
    public const string DebugOrgId = "e8f4ad87-4a4b-4da1-bcb2-1788eaeb80e8";
    public const int EmbeddedDataBlockLength = 256;
    public const long MaxUploadFileSize = 100_000_000;
    public const double ScriptRunExpirationMinutes = 30;
    public const string ApiKeyHeaderName = "X-Api-Key";
    public const string ExpiringTokenHeaderName = "X-Expiring-Token";
}
