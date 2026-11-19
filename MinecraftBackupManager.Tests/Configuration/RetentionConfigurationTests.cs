using System.Text.Json;
using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class RetentionConfigurationTests
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
        public void RetentionConfig_ShouldHaveAgreedDefaults()
        {
            var retention = new RetentionConfig();

            Assert.AreEqual(6, retention.HourlyCount);
            Assert.AreEqual(7, retention.DailyCount);
            Assert.AreEqual(4, retention.WeeklyCount);
        }

        [TestMethod]
        public void Validate_WithDefaultRetention_ShouldBeValid()
        {
            ConfigurationValidationResult result = new ConfigurationValidator().Validate(CreateValidConfig());

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
        }

        [TestMethod]
        [DataRow(1, 1, 1)]
        [DataRow(24, 7, 4)]
        public void Validate_WithRetentionAtLimits_ShouldBeValid(int hourly, int daily, int weekly)
        {
            ApplicationConfig config = CreateValidConfig(hourly, daily, weekly);

            ConfigurationValidationResult result = new ConfigurationValidator().Validate(config);

            Assert.IsTrue(result.IsValid, string.Join(" | ", result.Errors));
        }

        [TestMethod]
        [DataRow(0, 7, 4, "hourly")]
        [DataRow(25, 7, 4, "hourly")]
        [DataRow(6, 0, 4, "daily")]
        [DataRow(6, 8, 4, "daily")]
        [DataRow(6, 7, 0, "weekly")]
        [DataRow(6, 7, 5, "weekly")]
        public void Validate_WithRetentionOutOfRange_ShouldReportOnlyThatError(
            int hourly, int daily, int weekly, string expectedKeyword)
        {
            ApplicationConfig config = CreateValidConfig(hourly, daily, weekly);

            ConfigurationValidationResult result = new ConfigurationValidator().Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.HasCount(1, result.Errors);
            StringAssert.Contains(result.Errors[0], expectedKeyword);
        }

        [TestMethod]
        public void Validate_WithNullRetention_ShouldReportError()
        {
            ApplicationConfig config = CreateValidConfig();

            config.Retention = null!;

            ConfigurationValidationResult result = new ConfigurationValidator().Validate(config);

            Assert.IsFalse(result.IsValid);
            Assert.HasCount(1, result.Errors);
            StringAssert.Contains(result.Errors[0], "retención");
        }

        [TestMethod]
        public void Load_WithConfigFileWithoutRetention_ShouldUseDefaultRetention()
        {
            var paths = new ApplicationPaths(Path.Combine(_root, "data"));

            paths.EnsureDirectoriesExist();

            // Un config.json creado antes de que existiera la sección de retención.
            var oldFile = new
            {
                schemaVersion = 1,
                minecraftServer = new { serverDirectory = _serverDirectory, rconHost = "127.0.0.1", rconPort = 25575 },
                restic = new { executableSource = "bundled", repositoryPath = _repositoryPath }
            };

            File.WriteAllText(paths.ConfigurationFile, JsonSerializer.Serialize(oldFile));

            ApplicationConfig? config = new ConfigurationManager(paths).Load();

            Assert.IsNotNull(config);
            Assert.AreEqual(6, config.Retention.HourlyCount);
            Assert.AreEqual(7, config.Retention.DailyCount);
            Assert.AreEqual(4, config.Retention.WeeklyCount);
        }

        [TestMethod]
        public void SaveAndLoad_ShouldPreserveCustomRetention()
        {
            var paths = new ApplicationPaths(Path.Combine(_root, "data"));
            var manager = new ConfigurationManager(paths);

            manager.Save(CreateValidConfig(hourly: 12, daily: 3, weekly: 2));

            ApplicationConfig? loaded = manager.Load();

            Assert.IsNotNull(loaded);
            Assert.AreEqual(12, loaded.Retention.HourlyCount);
            Assert.AreEqual(3, loaded.Retention.DailyCount);
            Assert.AreEqual(2, loaded.Retention.WeeklyCount);
        }

        private ApplicationConfig CreateValidConfig(
            int hourly = RetentionConfig.DefaultHourlyCount,
            int daily = RetentionConfig.DefaultDailyCount,
            int weekly = RetentionConfig.DefaultWeeklyCount)
        {
            return new ApplicationConfig
            {
                MinecraftServer = new MinecraftServerConfig
                {
                    ServerDirectory = _serverDirectory,
                    RconHost = "127.0.0.1",
                    RconPort = 25575
                },
                Restic = new ResticConfig
                {
                    ExecutableSource = ResticExecutableSource.Bundled,
                    RepositoryPath = _repositoryPath
                },
                Retention = new RetentionConfig
                {
                    HourlyCount = hourly,
                    DailyCount = daily,
                    WeeklyCount = weekly
                }
            };
        }
    }
}
