using Remotely.Shared.Services;

namespace Remotely.Server.Services;

// Периодически проверяет устройства, которые давно не выходили на связь,
// и создаёт оповещения «Device offline» для пользователей организации.
public class DeviceOfflineAlertService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeviceOfflineAlertService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        // Дать серверу завершить старт и агентам переподключиться после перезапуска:
        // пока контейнер поднимается, все устройства числятся офлайн.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));

        try
        {
            do
            {
                await CheckDevices();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Application is shutting down.  Stopping device offline alert service.");
        }
    }

    private async Task CheckDevices()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
            var settings = await dataService.GetSettings();

            var createdCount = await dataService.GenerateOfflineDeviceAlerts(
                settings.OfflineDeviceAlertThresholdInDays);

            if (createdCount > 0)
            {
                logger.LogInformation("Created {count} offline device alert(s).", createdCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while generating offline device alerts.");
        }
    }
}
