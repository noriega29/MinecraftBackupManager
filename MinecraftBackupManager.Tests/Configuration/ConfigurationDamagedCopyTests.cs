using MinecraftBackupManager.Configuration;
using MinecraftBackupManager.Infrastructure;

namespace MinecraftBackupManager.Tests.Configuration
{
    [TestClass]
    public sealed class ConfigurationDamagedCopyTests
    {
        private static readonly DateTimeOffset Now = new(2026, 11, 27, 18, 30, 0, TimeSpan.Zero);

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
            _paths.EnsureDirectoriesExist();
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
        public void CopyDamagedFile_ShouldCreateADatedCopyAndKeepTheOriginal()
        {
            File.WriteAllText(_paths.ConfigurationFile, "{ roto");

            string copy = new ConfigurationManager(_paths).CopyDamagedFile(Now);

            Assert.AreEqual(_paths.ConfigurationFile + ".damaged-20261127-183000", copy);
            Assert.AreEqual("{ roto", File.ReadAllText(copy));
            Assert.AreEqual("{ roto", File.ReadAllText(_paths.ConfigurationFile));
        }

        [TestMethod]
        public void CopyDamagedFile_WhenTheCopyAlreadyExists_ShouldNotOverwriteIt()
        {
            var manager = new ConfigurationManager(_paths);

            File.WriteAllText(_paths.ConfigurationFile, "primero");

            string copy = manager.CopyDamagedFile(Now);

            File.WriteAllText(_paths.ConfigurationFile, "segundo");

            TestAssert.Throws<IOException>(() => manager.CopyDamagedFile(Now));

            Assert.AreEqual("primero", File.ReadAllText(copy));
        }

        [TestMethod]
        public void CopyDamagedFile_WithoutConfigurationFile_ShouldThrowFileNotFoundException()
        {
            TestAssert.Throws<FileNotFoundException>(
                () => new ConfigurationManager(_paths).CopyDamagedFile(Now));
        }
    }
}
