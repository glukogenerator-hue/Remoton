using Microsoft.VisualStudio.TestTools.UnitTesting;
using Remotely.Server.Services;
using Remotely.Shared.Entities;

namespace Remotely.Server.Tests;

[TestClass]
public class AgentHubSessionCacheTests
{
    [TestMethod]
    public void RemovingStaleConnection_KeepsDeviceOnlineViaNewConnection()
    {
        var cache = new AgentHubSessionCache();
        var device = new Device { ID = "dev-1" };

        cache.AddOrUpdateByConnectionId("old", device);
        // Агент переподключился раньше, чем сервер заметил обрыв старого соединения.
        cache.AddOrUpdateByConnectionId("new", device);

        Assert.IsTrue(cache.TryRemoveByConnectionId("old", out _));

        Assert.IsTrue(cache.TryGetConnectionId("dev-1", out var connectionId));
        Assert.AreEqual("new", connectionId);
        Assert.IsTrue(cache.TryGetByDeviceId("dev-1", out _));
    }

    [TestMethod]
    public void RemovingCurrentConnection_MakesDeviceOffline()
    {
        var cache = new AgentHubSessionCache();
        var device = new Device { ID = "dev-2" };

        cache.AddOrUpdateByConnectionId("conn", device);

        Assert.IsTrue(cache.TryRemoveByConnectionId("conn", out _));
        Assert.IsFalse(cache.TryGetConnectionId("dev-2", out _));
    }
}
