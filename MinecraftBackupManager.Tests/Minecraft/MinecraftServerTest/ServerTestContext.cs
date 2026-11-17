using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Security.Models;

namespace MinecraftBackupManager.Tests.Minecraft.MinecraftServerTest
{
    internal sealed class ServerTestContext : IAsyncDisposable
    {
        private readonly string _dataDirectory;

        public FakeRconServer FakeServer { get; }
        public MinecraftServer Server { get; }

        public ServerTestContext()
        {
            _dataDirectory = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            var paths = new ApplicationPaths(_dataDirectory);
            var credentialManager = new CredentialManager(paths);

            credentialManager.Save(new CredentialsData
            {
                RconPassword = FakeRconServer.Password,
                ResticRepositoryPassword = "test-repository-password"
            });

            FakeServer = new FakeRconServer();

            var config = new MinecraftServerConfig
            {
                RconHost = "127.0.0.1",
                RconPort = FakeServer.Port
            };

            Server = new MinecraftServer(config, credentialManager, new LogManager(paths));
        }

        public async ValueTask DisposeAsync()
        {
            // Primero MinecraftServer: si el guardado sigue desactivado, enviará
            // save-on, y el servidor falso todavía tiene que estar activo.
            await Server.DisposeAsync();
            await FakeServer.DisposeAsync();

            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}