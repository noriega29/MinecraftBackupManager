using System.Text.Json;
using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class ServerOfflineConfigurationTests
    {
        private string _root = string.Empty;
        private string _serverDirectory = string.Empty;
        private string _repositoryPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            _serverDirectory = Path.Combine(_root, "server");
            _repositoryPath = Path.Combine(_root, "repository");

            Directory.CreateDirectory(_serverDirectory);
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
        public void MinecraftServerConfig_ShouldAllowBackupWhenServerIsOffByDefault()
        {
            var config = new MinecraftServerConfig();

            Assert.IsTrue(config.AllowBackupWhenServerIsOff);
        }

        [TestMethod]
        public void Load_WithConfigFileWithoutTheOption_ShouldDefaultToAllowed()
        {
            var paths = new ApplicationPaths(Path.Combine(_root, "data"));

            paths.EnsureDirectoriesExist();

            // Un config.json creado antes de que existiera la opción.
            var oldFile = new
            {
                schemaVersion = 1,
                minecraftServer = new { serverDirectory = _serverDirectory, rconHost = "127.0.0.1", rconPort = 25575 },
                restic = new { executableSource = "bundled", repositoryPath = _repositoryPath }
            };

            File.WriteAllText(paths.ConfigurationFile, JsonSerializer.Serialize(oldFile));

            ApplicationConfig? config = new ConfigurationManager(paths).Load();

            Assert.IsNotNull(config);
            Assert.IsTrue(config.MinecraftServer.AllowBackupWhenServerIsOff);
        }

        [TestMethod]
        public void Load_WithOptionExplicitlyDisabled_ShouldReadFalse()
        {
            var paths = new ApplicationPaths(Path.Combine(_root, "data"));

            paths.EnsureDirectoriesExist();

            var file = new
            {
                schemaVersion = 1,
                minecraftServer = new
                {
                    serverDirectory = _serverDirectory,
                    rconHost = "127.0.0.1",
                    rconPort = 25575,
                    allowBackupWhenServerIsOff = false
                },
                restic = new { executableSource = "bundled", repositoryPath = _repositoryPath }
            };

            File.WriteAllText(paths.ConfigurationFile, JsonSerializer.Serialize(file));

            ApplicationConfig? config = new ConfigurationManager(paths).Load();

            Assert.IsNotNull(config);
            Assert.IsFalse(config.MinecraftServer.AllowBackupWhenServerIsOff);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void SaveAndLoad_ShouldPreserveTheOption(bool allowed)
        {
            var paths = new ApplicationPaths(Path.Combine(_root, "data"));
            var manager = new ConfigurationManager(paths);

            manager.Save(CreateValidConfig(allowed));

            ApplicationConfig? loaded = manager.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(allowed, loaded.MinecraftServer.AllowBackupWhenServerIsOff);
        }

        [TestMethod]
        public void Save_ShouldWriteTheOptionWithCamelCaseName()
        {
            var paths = new ApplicationPaths(Path.Combine(_root, "data"));

            new ConfigurationManager(paths).Save(CreateValidConfig(allowBackupWhenServerIsOff: false));

            string json = File.ReadAllText(paths.ConfigurationFile);

            StringAssert.Contains(json, "\"allowBackupWhenServerIsOff\": false");
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Validate_WithEitherOptionValue_ShouldBeValid(bool allowed)
        {
            ConfigurationValidationResult result = new ConfigurationValidator().Validate(CreateValidConfig(allowed));

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
        }

        private ApplicationConfig CreateValidConfig(bool allowBackupWhenServerIsOff)
        {
            return new ApplicationConfig
            {
                MinecraftServer = new MinecraftServerConfig
                {
                    ServerDirectory = _serverDirectory,
                    RconHost = "127.0.0.1",
                    RconPort = 25575,
                    AllowBackupWhenServerIsOff = allowBackupWhenServerIsOff
                },
                Restic = new ResticConfig
                {
                    ExecutableSource = ResticExecutableSource.Bundled,
                    RepositoryPath = _repositoryPath
                }
            };
        }
    }
}
