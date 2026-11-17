using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Security.Models;

namespace MinecraftBackupManager.Tests.Restic
{
    internal sealed class ResticTestContext : IDisposable
    {
        public const string RepositoryPassword = "test-repository-password";

        private readonly string _rootDirectory;
        private readonly CredentialManager _credentialManager;
        private readonly LogManager _logManager;
        private readonly string _logFilePath;
        private readonly string _backupLogFilePath;

        public string RepositoryPath { get; }
        public string SourcePath { get; }
        public ResticConfig Config { get; }
        public ResticClient Client { get; }

        public ResticTestContext()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_rootDirectory);

            // Datos de la aplicación (credenciales y logs) dentro de la carpeta temporal.
            var paths = new ApplicationPaths(Path.Combine(_rootDirectory, "Data"));

            _logFilePath = Path.Combine(paths.LogsDirectory, "application.log");
            _backupLogFilePath = Path.Combine(paths.LogsDirectory, "backup.log");

            _credentialManager = new CredentialManager(paths);
            _logManager = new LogManager(paths);

            _credentialManager.Save(new CredentialsData
            {
                ResticRepositoryPassword = RepositoryPassword
            });

            // Con un espacio a propósito: así se prueban rutas realistas.
            // El repositorio no se crea aquí; lo creará "restic init".
            RepositoryPath = Path.Combine(_rootDirectory, "Test Repository");

            // El "servidor falso" a respaldar, también con un espacio a propósito.
            SourcePath = Path.Combine(_rootDirectory, "Test Server");

            Directory.CreateDirectory(SourcePath);

            Config = new ResticConfig
            {
                ExecutableSource = ResticExecutableSource.Bundled,
                RepositoryPath = RepositoryPath
            };

            Client = CreateClient(Config);
        }

        public void SetRepositoryPassword(string password)
        {
            _credentialManager.Save(new CredentialsData { ResticRepositoryPassword = password });
        }

        public void DeleteCredentials()
        {
            _credentialManager.Delete();
        }

        public string ReadApplicationLog()
        {
            return File.Exists(_logFilePath) ? File.ReadAllText(_logFilePath) : string.Empty;
        }

        public string WriteSourceFile(string relativePath, string content)
        {
            string fullPath = Path.Combine(SourcePath, relativePath);

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);

            return fullPath;
        }

        public string ReadBackupLog()
        {
            return File.Exists(_backupLogFilePath) ? File.ReadAllText(_backupLogFilePath) : string.Empty;
        }

        // Único lugar donde se construye un ResticClient en las pruebas.
        public ResticClient CreateClient(ResticConfig config)
        {
            return new ResticClient(
                config,
                _credentialManager,
                new ResticExecutableLocator(),
                new ResticProcessRunner(),
                _logManager);
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
