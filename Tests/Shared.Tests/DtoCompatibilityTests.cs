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
}
