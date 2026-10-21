using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

[DataContract]
public class TogglePhysicalKeysDto
{
    [DataMember(Name = "ToggleOn")]
    public bool ToggleOn { get; set; }
}
