namespace Remotely.Server.Options;

public class ApplicationOptions
{
    public const string SectionKey = "ApplicationOptions";
    public string DbProvider { get; set; } = "SQLite";
    public string? DockerGatewayIp { get; set; }

    /// <summary>
    /// Включает Swagger UI (/swagger) вне среды разработки. По умолчанию выключено.
    /// </summary>
    public bool EnableSwagger { get; set; }
}
