namespace MinecraftBackupManager.Tests.Minecraft.MinecraftServerTest
{
    [TestClass]
    public sealed class MinecraftServerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        public async Task PrepareForBackupAsync_ShouldSendSaveOffAndSaveAllFlushInOrder()
        {
            await using var context = new ServerTestContext();

            await context.Server.PrepareForBackupAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "save-off", "save-all flush" },
                context.FakeServer.ReceivedCommands.ToArray());

            Assert.IsTrue(context.Server.IsAutosaveDisabled);
        }

        [TestMethod]
        public async Task ResumeAutosaveAsync_AfterPrepare_ShouldSendSaveOnAndClearDisabledFlag()
        {
            await using var context = new ServerTestContext();

            await context.Server.PrepareForBackupAsync().WaitAsync(TestTimeout);

            Assert.IsTrue(context.Server.IsAutosaveDisabled);

            await context.Server.ResumeAutosaveAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "save-off", "save-all flush", "save-on" },
                context.FakeServer.ReceivedCommands.ToArray());

            Assert.IsFalse(context.Server.IsAutosaveDisabled);
        }

        [TestMethod]
        public async Task PrepareForBackupAsync_ShouldUseSaveTimeoutForSaveAllFlush()
        {
            await using var context = new ServerTestContext();

            context.Server.CommandTimeout = TimeSpan.FromMilliseconds(300);
            context.Server.SaveTimeout = TimeSpan.FromSeconds(5);
            context.FakeServer.SetCommandDelay("save-all flush", TimeSpan.FromSeconds(1));

            await context.Server.PrepareForBackupAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "save-off", "save-all flush" },
                context.FakeServer.ReceivedCommands.ToArray());

            Assert.IsTrue(context.Server.IsAutosaveDisabled);
        }

        [TestMethod]
        public async Task ResumeAutosaveAsync_WithoutPrepare_ShouldStillSendSaveOn()
        {
            await using var context = new ServerTestContext();

            Assert.IsFalse(context.Server.IsAutosaveDisabled);

            await context.Server.ResumeAutosaveAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "save-on" },
                context.FakeServer.ReceivedCommands.ToArray());

            Assert.IsFalse(context.Server.IsAutosaveDisabled);
        }

        [TestMethod]
        public async Task PrepareForBackupAsync_WhenSaveAllFlushTimesOut_ShouldResumeAutosaveOnNewConnection()
        {
            await using var context = new ServerTestContext();

            context.Server.SaveTimeout = TimeSpan.FromMilliseconds(300);
            context.FakeServer.SetCommandDelay("save-all flush", TimeSpan.FromSeconds(3));

            TimeoutException? exception = null;

            try
            {
                await context.Server.PrepareForBackupAsync().WaitAsync(TestTimeout);
            }
            catch (TimeoutException caught)
            {
                exception = caught;
            }

            Assert.IsNotNull(exception, "Se esperaba un TimeoutException.");

            CollectionAssert.AreEqual(
                new[] { "save-off", "save-all flush", "save-on" },
                context.FakeServer.ReceivedCommands.ToArray());

            Assert.AreEqual(2, context.FakeServer.ConnectionCount);
            Assert.IsFalse(context.Server.IsAutosaveDisabled);
        }

        [TestMethod]
        public async Task ResumeAutosaveAsync_WhenConnectionWasClosedByServer_ShouldReconnectAndSendSaveOn()
        {
            await using var context = new ServerTestContext();

            await context.Server.ConnectAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(1, context.FakeServer.ConnectionCount);

            context.FakeServer.CloseAllConnections();

            await context.Server.ResumeAutosaveAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "save-on" },
                context.FakeServer.ReceivedCommands.ToArray());

            Assert.AreEqual(2, context.FakeServer.ConnectionCount);
            Assert.IsFalse(context.Server.IsAutosaveDisabled);
        }
    }
}
