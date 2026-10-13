using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

[DataContract]
public class KeyDownDto
{
    [DataMember(Name = "Key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Физическая клавиша (KeyboardEvent.code), не зависящая от раскладки.
    /// Может отсутствовать у старых клиентов.
    /// </summary>
    [DataMember(Name = "Code")]
    public string? Code { get; set; }
}
