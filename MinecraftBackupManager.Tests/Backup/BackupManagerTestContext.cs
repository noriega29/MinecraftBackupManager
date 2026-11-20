using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;

namespace MinecraftBackupManager.Tests.Backup
{
    internal sealed class BackupManagerTestContext : IDisposable
    {
        private readonly string _rootDirectory;

        public BackupCallLog Log { get; } = new();

        public FakeBackupServer Server { get; }

        public FakeBackupRepository Repository { get; }

        public FakePruneStateStore PruneState { get; }

        public RetentionConfig Retention { get; }

        // Ruta del "servidor" a respaldar. No se crea: los sustitutos no la usan.
        public string ServerDirectory { get; }

        public BackupManager Manager { get; }

        public BackupManagerTestContext()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            var paths = new ApplicationPaths(Path.Combine(_rootDirectory, "Data"));

            // Con un espacio a propósito, como en las demás pruebas.
            ServerDirectory = Path.Combine(_rootDirectory, "Test Server");

            Server = new FakeBackupServer(Log);
            Repository = new FakeBackupRepository(Log);
            PruneState = new FakePruneStateStore(Log);
            Retention = new RetentionConfig();

            var serverConfig = new MinecraftServerConfig
            {
                ServerDirectory = ServerDirectory
            };

            Manager = new BackupManager(
                Server,
                Repository,
                PruneState,
                serverConfig,
                Retention,
                new LogManager(paths));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
