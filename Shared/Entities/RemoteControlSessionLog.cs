using System.ComponentModel.DataAnnotations;

namespace Remotely.Shared.Entities;

/// <summary>
/// Журнал сеансов удалённого управления: кто, когда и откуда подключался
/// к устройству. Хранится без внешних ключей, чтобы записи не пропадали
/// при удалении устройства или пользователя.
/// </summary>
public class RemoteControlSessionLog
{
    [Key]
    public string ID { get; set; } = Guid.NewGuid().ToString();

    public DateTimeOffset StartedOn { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset? EndedOn { get; set; }

    public string DeviceID { get; set; } = string.Empty;

    /// <summary>
    /// Снимок имени устройства на момент сеанса.
    /// </summary>
    public string DeviceName { get; set; } = string.Empty;

    public string OrganizationID { get; set; } = string.Empty;

    /// <summary>
    /// Учётная запись наблюдателя, если он был аутентифицирован.
    /// </summary>
    public string? ViewerUserName { get; set; }

    /// <summary>
    /// Имя, которое наблюдатель указал на форме быстрой поддержки.
    /// </summary>
    public string? RequesterDisplayName { get; set; }

    public string? ViewerIPAddress { get; set; }

    /// <summary>
    /// «Attended» или «Unattended».
    /// </summary>
    public string Mode { get; set; } = string.Empty;

    public bool ViewOnly { get; set; }
}
