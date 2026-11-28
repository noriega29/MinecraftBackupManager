using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class ConfigurationChangeCheckerTests
    {
        private readonly ConfigurationChangeChecker _checker = new(new ConfigurationValidator());
        private string _root = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_root);
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
        public void Check_WithValidServerDirectory_ShouldAcceptWithoutChangingTheOriginal()
        {
            string server = CreateServer("Test Server");
            var current = new ApplicationConfig();

            ConfigurationChangeResult result = ChangeServer(current, server);

            Assert.IsTrue(result.IsAccepted);
            Assert.AreEqual(server, result.Config.MinecraftServer.ServerDirectory);
            Assert.AreEqual(string.Empty, current.MinecraftServer.ServerDirectory);
        }

        [TestMethod]
        public void Check_WithoutServerProperties_ShouldReject()
        {
            string folder = Path.Combine(_root, "Vacia");

            Directory.CreateDirectory(folder);

            ConfigurationChangeResult result = ChangeServer(new ApplicationConfig(), folder);

            Assert.IsFalse(result.IsAccepted);
            StringAssert.Contains(result.Errors[0], "server.properties");
        }

        [TestMethod]
        public void Check_ReplacingAMissingFolderWithAnotherMissingFolder_ShouldReject()
        {
            var current = new ApplicationConfig();

            current.MinecraftServer.ServerDirectory = Path.Combine(_root, "borrada");

            ConfigurationChangeResult result = ChangeServer(current, Path.Combine(_root, "tampoco-existe"));

            Assert.IsFalse(result.IsAccepted);
        }

        [TestMethod]
        public void Check_WhenOtherDataIsInvalid_ShouldStillAcceptAValidChange()
        {
            var current = new ApplicationConfig();

            current.MinecraftServer.ServerDirectory = Path.Combine(_root, "borrada");

            ConfigurationChangeResult result = _checker.Check(
                current, ConfigurationField.RconPort, config => config.MinecraftServer.RconPort = 25576);

            Assert.IsTrue(result.IsAccepted);
        }

        [TestMethod]
        public void Check_WithRepositoryInsideServer_ShouldReject()
        {
            var current = new ApplicationConfig();

            current.MinecraftServer.ServerDirectory = CreateServer("Test Server");

            ConfigurationChangeResult result = ChangeRepository(
                current, Path.Combine(current.MinecraftServer.ServerDirectory, "Copias"));

            Assert.IsFalse(result.IsAccepted);
        }

        [TestMethod]
        public void Check_WithServerInsideRepository_ShouldReject()
        {
            string server = CreateServer("Test Server");
            var current = new ApplicationConfig();

            current.Restic.RepositoryPath = _root;

            Assert.IsFalse(ChangeServer(current, server).IsAccepted);
        }

        [TestMethod]
        public void Check_WithRelativeRepositoryPath_ShouldReject()
        {
            Assert.IsFalse(ChangeRepository(new ApplicationConfig(), "Copias").IsAccepted);
        }

        [TestMethod]
        [DataRow(0, false)]
        [DataRow(25575, true)]
        [DataRow(65536, false)]
        public void Check_RconPort_ShouldRespectTheRange(int port, bool expected)
        {
            ConfigurationChangeResult result = _checker.Check(
                new ApplicationConfig(), ConfigurationField.RconPort, config => config.MinecraftServer.RconPort = port);

            Assert.AreEqual(expected, result.IsAccepted);
        }

        [TestMethod]
        [DataRow(0, false)]
        [DataRow(24, true)]
        [DataRow(25, false)]
        public void Check_HourlyCount_ShouldRespectTheRange(int count, bool expected)
        {
            ConfigurationChangeResult result = _checker.Check(
                new ApplicationConfig(), ConfigurationField.HourlyCount, config => config.Retention.HourlyCount = count);

            Assert.AreEqual(expected, result.IsAccepted);
        }

        [TestMethod]
        public void Check_WithEmptyRconHost_ShouldReject()
        {
            ConfigurationChangeResult result = _checker.Check(
                new ApplicationConfig(), ConfigurationField.RconHost, config => config.MinecraftServer.RconHost = "");

            Assert.IsFalse(result.IsAccepted);
        }

        [TestMethod]
        public void Check_WithUnknownField_ShouldThrowArgumentOutOfRangeException()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => _checker.Check(new ApplicationConfig(), (ConfigurationField)99, _ => { }));
        }

        [TestMethod]
        public void Check_WithNullArguments_ShouldThrowArgumentNullException()
        {
            TestAssert.Throws<ArgumentNullException>(() => new ConfigurationChangeChecker(null!));

            TestAssert.Throws<ArgumentNullException>(
                () => _checker.Check(null!, ConfigurationField.RconPort, _ => { }));

            TestAssert.Throws<ArgumentNullException>(
                () => _checker.Check(new ApplicationConfig(), ConfigurationField.RconPort, null!));
        }

        private ConfigurationChangeResult ChangeServer(ApplicationConfig current, string path)
        {
            return _checker.Check(
                current, ConfigurationField.ServerDirectory, config => config.MinecraftServer.ServerDirectory = path);
        }

        private ConfigurationChangeResult ChangeRepository(ApplicationConfig current, string path)
        {
            return _checker.Check(
                current, ConfigurationField.RepositoryPath, config => config.Restic.RepositoryPath = path);
        }

        private string CreateServer(string name)
        {
            string directory = Path.Combine(_root, name);

            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "server.properties"), "level-name=world\n");

            return directory;
        }
    }
}
