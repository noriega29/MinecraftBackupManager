using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Minecraft.MinecraftServerTest
{
    [TestClass]
    public sealed class FakeRconServerTests
    {
        [TestMethod]
        public async Task FakeRconServer_ShouldAuthenticateAndRecordCommands()
        {
            await using var fake = new FakeRconServer();
            using var client = new RconClient("127.0.0.1", fake.Port);

            await client.ConnectAsync();
            await client.AuthenticateAsync(FakeRconServer.Password);

            string response = await client.ExecuteCommandAsync("list").WaitAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(FakeRconServer.CommandResponse, response);
            CollectionAssert.AreEqual(new[] { "list" }, fake.ReceivedCommands.ToArray());
        }
    }
}
