using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Security.Models;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class SetupStatusCheckerTests
    {
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
        public void Check_WithoutConfigurationFile_ShouldReturnNotConfiguredWithoutCreatingFiles()
        {
            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.NotConfigured, status.State);
            Assert.IsNull(status.Config);
            Assert.IsFalse(Directory.Exists(_paths.DataDirectory));
        }

        [TestMethod]
        [DataRow("")]
        [DataRow("esto no es JSON")]
        [DataRow("null")]
        public void Check_WithDamagedFile_ShouldReturnConfigurationDamaged(string content)
        {
            WriteConfigurationFile(content);

            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.ConfigurationDamaged, status.State);
            Assert.IsNull(status.Config);
            Assert.IsNotEmpty(status.Errors);
        }

        [TestMethod]
        public void Check_WithUnknownSchemaVersion_ShouldReturnConfigurationUnsupported()
        {
            WriteConfigurationFile("{\"schemaVersion\": 99}");

            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.ConfigurationUnsupported, status.State);
            Assert.IsNull(status.Config);
            Assert.IsNotEmpty(status.Errors);
        }

        [TestMethod]
        public void Check_WithInvalidConfigurationAndNoCredentials_ShouldReportTheConfigurationFirst()
        {
            ApplicationConfig config = CreateValidConfig();

            config.MinecraftServer.ServerDirectory = Path.Combine(_root, "no-existe");

            SaveConfiguration(config);

            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.ConfigurationInvalid, status.State);
            Assert.IsNotNull(status.Config);
            Assert.IsNotEmpty(status.Errors);
        }

        [TestMethod]
        public void Check_WithoutCredentials_ShouldReturnCredentialsMissing()
        {
            SaveConfiguration(CreateValidConfig());

            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.CredentialsMissing, status.State);
            Assert.IsNotNull(status.Config);
        }

        [TestMethod]
        [DataRow("", "repositorio")]
        [DataRow("rcon", "")]
        public void Check_WithAnEmptyPassword_ShouldReturnCredentialsMissing(string rcon, string repository)
        {
            SaveConfiguration(CreateValidConfig());
            SaveCredentials(rcon, repository);

            Assert.AreEqual(SetupState.CredentialsMissing, CreateChecker().Check().State);
        }

        [TestMethod]
        public void Check_WithDamagedCredentials_ShouldReturnCredentialsUnreadable()
        {
            SaveConfiguration(CreateValidConfig());

            File.WriteAllBytes(_paths.CredentialsFile, new byte[] { 1, 2, 3, 4 });

            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.CredentialsUnreadable, status.State);
            Assert.IsNotNull(status.Config);
            Assert.IsNotEmpty(status.Errors);
        }

        [TestMethod]
        public void Check_WithEverythingCorrect_ShouldReturnReady()
        {
            SaveConfiguration(CreateValidConfig());
            SaveCredentials("rcon", "repositorio");

            SetupStatus status = CreateChecker().Check();

            Assert.AreEqual(SetupState.Ready, status.State);
            Assert.IsNotNull(status.Config);
            Assert.IsEmpty(status.Errors);
        }

        [TestMethod]
        public void Check_WhenConfigurationCannotBeRead_ShouldThrowIOException()
        {
            SaveConfiguration(CreateValidConfig());

            using var holder = new FileStream(_paths.ConfigurationFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            TestAssert.Throws<IOException>(() => CreateChecker().Check());
        }

        [TestMethod]
        public void Constructor_WithNullArguments_ShouldThrowArgumentNullException()
        {
            var manager = new ConfigurationManager(_paths);
            var validator = new ConfigurationValidator();
            var credentials = new CredentialManager(_paths);

            TestAssert.Throws<ArgumentNullException>(() => new SetupStatusChecker(null!, validator, credentials));
            TestAssert.Throws<ArgumentNullException>(() => new SetupStatusChecker(manager, null!, credentials));
            TestAssert.Throws<ArgumentNullException>(() => new SetupStatusChecker(manager, validator, null!));
        }

        private SetupStatusChecker CreateChecker()
        {
            return new SetupStatusChecker(
                new ConfigurationManager(_paths),
                new ConfigurationValidator(),
                new CredentialManager(_paths));
        }

        private ApplicationConfig CreateValidConfig()
        {
            string serverDirectory = Path.Combine(_root, "Test Server");

            Directory.CreateDirectory(serverDirectory);

            var config = new ApplicationConfig();

            config.MinecraftServer.ServerDirectory = serverDirectory;
            config.Restic.RepositoryPath = Path.Combine(_root, "Repository");

            return config;
        }

        private void SaveConfiguration(ApplicationConfig config)
        {
            new ConfigurationManager(_paths).Save(config);
        }

        private void SaveCredentials(string rconPassword, string repositoryPassword)
        {
            new CredentialManager(_paths).Save(new CredentialsData
            {
                RconPassword = rconPassword,
                ResticRepositoryPassword = repositoryPassword
            });
        }

        private void WriteConfigurationFile(string content)
        {
            _paths.EnsureDirectoriesExist();

            File.WriteAllText(_paths.ConfigurationFile, content);
        }
    }
}
