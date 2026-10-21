using MessagePack;
using Remotely.Shared.Models.Dtos;
using System.Runtime.Serialization;

namespace Remotely.Shared.Tests;

/// <summary>
/// Проверяет, что агенты разных версий понимают друг друга: новое необязательное поле Code
/// не должно ломать старые агенты, а сообщения старых клиентов должны читаться новыми.
/// </summary>
[TestClass]
public class DtoCompatibilityTests
{
    // Точная копия KeyDownDto и KeyUpDto до добавления поля Code.
    [DataContract]
    public class LegacyKeyDownDto
    {
        [DataMember(Name = "Key")]
        public string Key { get; set; } = string.Empty;
    }

    [DataContract]
    public class LegacyKeyUpDto
    {
        [DataMember(Name = "Key")]
        public string Key { get; set; } = string.Empty;
    }

    [TestMethod]
    public void NewKeyDown_IsReadableByLegacyAgent()
    {
        var bytes = MessagePackSerializer.Serialize(new KeyDownDto { Key = "ф", Code = "KeyA" });

        var legacy = MessagePackSerializer.Deserialize<LegacyKeyDownDto>(bytes);

        Assert.AreEqual("ф", legacy.Key);
    }

    [TestMethod]
    public void NewKeyUp_IsReadableByLegacyAgent()
    {
        var bytes = MessagePackSerializer.Serialize(new KeyUpDto { Key = "c", Code = "KeyC" });

        var legacy = MessagePackSerializer.Deserialize<LegacyKeyUpDto>(bytes);

        Assert.AreEqual("c", legacy.Key);
    }

    [TestMethod]
    public void LegacyKeyDown_IsReadableByNewAgent_WithoutCode()
    {
        var bytes = MessagePackSerializer.Serialize(new LegacyKeyDownDto { Key = "a" });

        var current = MessagePackSerializer.Deserialize<KeyDownDto>(bytes);

        Assert.AreEqual("a", current.Key);
        Assert.IsNull(current.Code);
    }

    [TestMethod]
    public void KeyDownWithoutCode_RoundTrips()
    {
        var bytes = MessagePackSerializer.Serialize(new KeyDownDto { Key = "Enter" });

        var current = MessagePackSerializer.Deserialize<KeyDownDto>(bytes);

        Assert.AreEqual("Enter", current.Key);
        Assert.IsNull(current.Code);
    }

    // Точная копия SessionMetricsDto до добавления поля ImageQuality.
    [DataContract]
    public class LegacySessionMetricsDto
    {
        [DataMember(Name = "Mbps")]
        public double Mbps { get; set; }

        [DataMember(Name = "Fps")]
        public double Fps { get; set; }

        [DataMember(Name = "RoundTripLatency")]
        public double RoundTripLatency { get; set; }

        [DataMember(Name = "IsGpuAccelerated")]
        public bool IsGpuAccelerated { get; set; }
    }

    [TestMethod]
    public void SessionMetrics_RoundTripsWithImageQuality()
    {
        var bytes = MessagePackSerializer.Serialize(new SessionMetricsDto(3.5, 24, 42, true, 65));

        var current = MessagePackSerializer.Deserialize<SessionMetricsDto>(bytes);

        Assert.AreEqual(3.5, current.Mbps);
        Assert.AreEqual(24, current.Fps);
        Assert.AreEqual(42, current.RoundTripLatency);
        Assert.IsTrue(current.IsGpuAccelerated);
        Assert.AreEqual(65, current.ImageQuality);
    }

    [TestMethod]
    public void NewSessionMetrics_IsReadableByLegacyViewer()
    {
        var bytes = MessagePackSerializer.Serialize(new SessionMetricsDto(1.25, 10, 80, false, 40));

        var legacy = MessagePackSerializer.Deserialize<LegacySessionMetricsDto>(bytes);

        Assert.AreEqual(1.25, legacy.Mbps);
        Assert.AreEqual(10, legacy.Fps);
        Assert.AreEqual(80, legacy.RoundTripLatency);
    }

    [TestMethod]
    public void LegacySessionMetrics_IsReadableByNewViewer_WithoutImageQuality()
    {
        var bytes = MessagePackSerializer.Serialize(new LegacySessionMetricsDto
        {
            Mbps = 2,
            Fps = 30,
            RoundTripLatency = 15,
            IsGpuAccelerated = true
        });

        var current = MessagePackSerializer.Deserialize<SessionMetricsDto>(bytes);

        Assert.AreEqual(30, current.Fps);
        Assert.AreEqual(0, current.ImageQuality);
    }
}
