using MessagePack;
using System.Runtime.Serialization;

namespace Remotely.Shared.Models.Dtos;

[DataContract]
public class SessionMetricsDto
{
    [SerializationConstructor]
    public SessionMetricsDto(double mbps, double fps, double roundTripLatency, bool isGpuAccelerated, int imageQuality = 0)
    {
        Mbps = mbps;
        Fps = fps;
        RoundTripLatency = roundTripLatency;
        IsGpuAccelerated = isGpuAccelerated;
        ImageQuality = imageQuality;
    }

    [DataMember]
    public double Mbps { get; }

    [DataMember]
    public double Fps { get; }

    [DataMember]
    public double RoundTripLatency { get; }

    [DataMember]
    public bool IsGpuAccelerated { get; }

    /// <summary>
    /// Текущее качество JPEG (35-80). Снижается, когда канал не справляется; 0 — агент не сообщил.
    /// </summary>
    [DataMember]
    public int ImageQuality { get; }
}
