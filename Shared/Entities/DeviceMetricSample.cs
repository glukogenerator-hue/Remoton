using System.ComponentModel.DataAnnotations;

namespace Remotely.Shared.Entities;

/// <summary>
/// Точка истории загрузки устройства. Записывается не чаще раза в 15 минут
/// при обновлении состояния устройства, хранится без внешних ключей
/// и удаляется по сроку хранения данных.
/// </summary>
public class DeviceMetricSample
{
    [Key]
    public string ID { get; set; } = Guid.NewGuid().ToString();

    public string DeviceID { get; set; } = string.Empty;

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Загрузка процессора, 0..100.
    /// </summary>
    public double CpuUtilization { get; set; }

    /// <summary>
    /// Использовано памяти, 0..1.
    /// </summary>
    public double UsedMemoryPercent { get; set; }

    /// <summary>
    /// Использовано диска (суммарно по всем дискам), 0..1.
    /// </summary>
    public double UsedStoragePercent { get; set; }
}
