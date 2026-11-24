using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Remotely.Server.Hubs;
using Remotely.Server.RateLimiting;
using Remotely.Server.Services;
using Remotely.Shared.Interfaces;
using Remotely.Shared.Entities;
using Remotely.Server.Models;
using System.Collections.Concurrent;
using System.Net;

namespace Remotely.Server.API;

[Route("api/[controller]")]
[ApiController]
public class AgentUpdateController : ControllerBase
{
    private readonly IHubContext<AgentHub, IAgentHubClient> _agentHubContext;
    private readonly ILogger<AgentUpdateController> _logger;
    private readonly IDataService _dataService;
    private readonly IWebHostEnvironment _hostEnv;
    private readonly IAgentHubSessionCache _serviceSessionCache;

    public AgentUpdateController(IWebHostEnvironment hostingEnv,
        IDataService dataService,
        IAgentHubSessionCache serviceSessionCache,
        IHubContext<AgentHub, IAgentHubClient> agentHubContext,
        ILogger<AgentUpdateController> logger)
    {
        _hostEnv = hostingEnv;
        _dataService = dataService;
        _serviceSessionCache = serviceSessionCache;
        _agentHubContext = agentHubContext;
        _logger = logger;
    }

    [HttpGet("[action]/{platform}")]
    [EnableRateLimiting(PolicyNames.AgentUpdateDownloads)]
    public async Task<ActionResult> DownloadPackage(string platform)
    {
        try
        {
            var remoteIp = $"{Request?.HttpContext?.Connection?.RemoteIpAddress}";

            if (await CheckForDeviceBan(remoteIp))
            {
                return BadRequest();
            }

            string filePath;

            switch (platform.ToLower())
            {
                case "win-x64":
                    filePath = Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-Win-x64.zip");
                    break;
                case "win-x86":
                    filePath = Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-Win-x86.zip");
                    break;
                case "linux":
                    filePath = Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-Linux.zip");
                    break;
                case "macos-x64":
                    filePath = Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-MacOS-x64.zip");
                    break;
                default:
                    _logger.LogWarning(
                        "Unknown platform requested in {className}. " +
                        "Platform: {platform}. " +
                        "IP: {remoteIp}.",
                        nameof(AgentUpdateController),
                        platform,
                        remoteIp);
                    return BadRequest();
            }

            var fileStream = System.IO.File.OpenRead(filePath);

            return File(fileStream, "application/octet-stream", "RemotelyUpdate.zip");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while downloading package.");
            return StatusCode((int)HttpStatusCode.InternalServerError);
        }
    }


    [HttpGet("[action]/{platform}/{downloadId}")]
    [EnableRateLimiting(PolicyNames.AgentUpdateDownloads)]
    [Obsolete("This method is only for backwards compatibility.  Remove after a few releases.")]
    public async Task<ActionResult> DownloadPackage(string platform, string downloadId)
    {
        return await DownloadPackage(platform);
    }

    // Точка проверки обновления для агентов с поддержкой поэтапной раскатки.
    // Агент вызывает её вместо HEAD на статический файл /Content/*.zip и передаёт
    // свой DeviceId. Если в Server Config заданы группы волны, устройство вне этих
    // групп всегда получает 304 — оно не обновится, пока его группу не добавят в волну.
    // Старые агенты продолжают проверять статический файл и обновляются без ограничений.
    // ВАЖНО: агент проверяет обновление методом HEAD. Объявляем и GET, и HEAD,
    // иначе HEAD получает 405 и агент трактует это как «есть обновление».
    [AcceptVerbs("GET", "HEAD", Route = "[action]/{platform}")]
    public async Task<ActionResult> CheckPackage(string platform, [FromQuery] string? deviceId)
    {
        // Агент считает «есть обновление» любым ответом кроме 304 и запускает
        // переустановку. Поэтому при любой внутренней проблеме безопаснее ответить
        // «обновлений нет»: машина продолжит работать на текущей версии.
        try
        {
            if (!TryGetPackagePath(platform, out var filePath))
            {
                _logger.LogWarning("Unknown platform requested in CheckPackage: {platform}.", platform);
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            var settings = await _dataService.GetSettings();

            Device? device = null;
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var deviceResult = await _dataService.GetDevice(
                    deviceId,
                    query => query.Include(x => x.DeviceGroup));

                if (deviceResult.IsSuccess)
                {
                    device = deviceResult.Value;
                }
            }

            if (!IsDeviceInUpdateWave(device, settings))
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            var etagValue = GetPackageEtag(filePath);

            // Если-None-Match приходит от агента как сохранённый etag.txt; сравниваем
            // строки, допуская слабые etag с префиксом W/.
            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) &&
                ifNoneMatch
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(x => x.Equals(etagValue, StringComparison.Ordinal) ||
                              x.Equals("W/" + etagValue, StringComparison.Ordinal)))
            {
                return StatusCode((int)HttpStatusCode.NotModified);
            }

            Response.Headers.ETag = etagValue;
            return File(System.IO.File.OpenRead(filePath), "application/octet-stream");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while checking package for update.  Treating as no update available.");
            return StatusCode((int)HttpStatusCode.NotModified);
        }
    }

    private bool TryGetPackagePath(string platform, out string filePath)
    {
        filePath = platform.ToLower() switch
        {
            "win-x64" => Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-Win-x64.zip"),
            "win-x86" => Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-Win-x86.zip"),
            "linux" => Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-Linux.zip"),
            "macos-x64" => Path.Combine(_hostEnv.WebRootPath, "Content", "Remotely-MacOS-x64.zip"),
            _ => string.Empty
        };

        return !string.IsNullOrEmpty(filePath);
    }

    private bool IsDeviceInUpdateWave(Device? device, SettingsModel settings)
    {
        // Группы волны не заданы — обновляются все устройства (прежнее поведение).
        if (settings.AgentUpdateWaveDeviceGroups is not { Count: > 0 })
        {
            return true;
        }

        if (device is null)
        {
            // Проверка без идентификатора (curl, тесты) или неизвестное устройство —
            // не ограничиваем, чтобы не блокировать свежие установки.
            return true;
        }

        if (settings.BannedDevices.Contains(device.ID, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return device.DeviceGroup is not null &&
            settings.AgentUpdateWaveDeviceGroups.Contains(
                device.DeviceGroup.Name,
                StringComparer.OrdinalIgnoreCase);
    }

    // ETag считаем по содержимому пакета, чтобы значение пережило перезапуск сервера.
    // Полный хеш 100-МБ файла считается один раз и кэшируется до изменения размера
    // или времени изменения файла.
    private static readonly ConcurrentDictionary<string, (string EtagValue, DateTime MTime, long Length)> _packageEtags = new();

    private static string GetPackageEtag(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        var cacheKey = Path.GetFileName(filePath);

        if (_packageEtags.TryGetValue(cacheKey, out var cached) &&
            cached.MTime == fileInfo.LastWriteTimeUtc &&
            cached.Length == fileInfo.Length)
        {
            return cached.EtagValue;
        }

        using var stream = fileInfo.OpenRead();
        var hash = System.Security.Cryptography.SHA256.HashData(stream);
        var etagValue = $"\"pkg-{Convert.ToHexString(hash)[..32]}\"";

        _packageEtags[cacheKey] = (etagValue, fileInfo.LastWriteTimeUtc, fileInfo.Length);

        return etagValue;
    }

    private async Task<bool> CheckForDeviceBan(string deviceIp)
    {
        if (string.IsNullOrWhiteSpace(deviceIp))
        {
            return false;
        }

        var settings = await _dataService.GetSettings();
        if (settings.BannedDevices.Contains(deviceIp))
        {
            _logger.LogInformation("Device IP ({deviceIp}) is banned.  Sending uninstall command.", deviceIp);

            
            var bannedDevices = _serviceSessionCache.GetAllDevices().Where(x => x.PublicIP == deviceIp);
            var connectionIds = _serviceSessionCache.GetConnectionIdsByDeviceIds(bannedDevices.Select(x => x.ID));
            await _agentHubContext.Clients.Clients(connectionIds).UninstallAgent();

            return true;
        }

        return false;
    }
}
