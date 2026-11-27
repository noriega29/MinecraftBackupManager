using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Core;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Restic.Models;
using MinecraftBackupManager.Security;

namespace MinecraftBackupManager.Tests.Core
{
    [TestClass]
    public sealed class BackupServicesTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        private string _root = string.Empty;
        private ApplicationPaths _paths = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            _paths = new ApplicationPaths(Path.Combine(_root, "Data"));
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [TestMethod]
        public async Task Constructor_ShouldExposeServicesWithoutCreatingFiles()
        {
            await using BackupServices services = CreateServices();

            Assert.IsNotNull(services.Manager);
            Assert.IsNotNull(services.Scheduler);
            Assert.IsNotNull(services.Repository);

            // Construir no debe escribir nada: ni logs ni archivos de estado.
            Assert.IsFalse(Directory.Exists(_paths.DataDirectory));
        }

        [TestMethod]
        public async Task DisposeAsync_ShouldRejectNewBackupsAndSchedulerStart()
        {
            BackupServices services = CreateServices();

            await services.DisposeAsync().AsTask().WaitAsync(TestTimeout);

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => services.Manager.RunBackupAsync(ResticBackupKind.Manual));

            TestAssert.Throws<ObjectDisposedException>(() => services.Scheduler.Start());
        }

        [TestMethod]
        public async Task DisposeAsync_CalledTwiceAtOnce_ShouldCompleteBoth()
        {
            BackupServices services = CreateServices();

            Task first = services.DisposeAsync().AsTask();
            Task second = services.DisposeAsync().AsTask();

            await Task.WhenAll(first, second).WaitAsync(TestTimeout);
        }

        [TestMethod]
        public void Constructor_WithNullArguments_ShouldThrowArgumentNullException()
        {
            var config = new ApplicationConfig();
            var logManager = new LogManager(_paths);
            var credentialManager = new CredentialManager(_paths);

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupServices(null!, _paths, logManager, credentialManager));

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupServices(config, null!, logManager, credentialManager));

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupServices(config, _paths, null!, credentialManager));

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupServices(config, _paths, logManager, null!));
        }

        private BackupServices CreateServices()
        {
            return new BackupServices(
                new ApplicationConfig(),
                _paths,
                new LogManager(_paths),
                new CredentialManager(_paths));
        }
    }
}
