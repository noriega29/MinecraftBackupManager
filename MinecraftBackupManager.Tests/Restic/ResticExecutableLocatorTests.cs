using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Restic;

namespace MinecraftBackupManager.Tests.Restic
{
    [TestClass]
    public sealed class ResticExecutableLocatorTests
    {
        private string _baseDirectory = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _baseDirectory = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(_baseDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                Directory.Delete(_baseDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [TestMethod]
        public void Resolve_WithBundledSource_ShouldReturnPathInsideToolsDirectory()
        {
            string expectedPath = Path.Combine(_baseDirectory, "Tools", "restic.exe");

            CreateEmptyFile(expectedPath);

            var locator = new ResticExecutableLocator(_baseDirectory);
            var config = new ResticConfig { ExecutableSource = ResticExecutableSource.Bundled };

            string result = locator.Resolve(config);

            Assert.AreEqual(expectedPath, result);
        }

        [TestMethod]
        public void Resolve_WithBundledSource_WhenFileIsMissing_ShouldThrowFileNotFoundException()
        {
            string expectedPath = Path.Combine(_baseDirectory, "Tools", "restic.exe");

            var locator = new ResticExecutableLocator(_baseDirectory);
            var config = new ResticConfig { ExecutableSource = ResticExecutableSource.Bundled };

            FileNotFoundException exception = AssertThrows<FileNotFoundException>(
                () => locator.Resolve(config));

            Assert.AreEqual(expectedPath, exception.FileName);
        }

        // ============================================================
        // FUENTE PERSONALIZADA
        // ============================================================

        [TestMethod]
        public void Resolve_WithCustomSource_ShouldReturnConfiguredPath()
        {
            string customPath = Path.Combine(_baseDirectory, "Elsewhere", "my-restic.exe");

            CreateEmptyFile(customPath);

            var locator = new ResticExecutableLocator(_baseDirectory);
            var config = new ResticConfig
            {
                ExecutableSource = ResticExecutableSource.Custom,
                CustomExecutablePath = customPath
            };

            string result = locator.Resolve(config);

            Assert.AreEqual(customPath, result);
        }

        [TestMethod]
        public void Resolve_WithCustomSource_WhenFileIsMissing_ShouldThrowFileNotFoundException()
        {
            string customPath = Path.Combine(_baseDirectory, "Elsewhere", "missing-restic.exe");

            var locator = new ResticExecutableLocator(_baseDirectory);
            var config = new ResticConfig
            {
                ExecutableSource = ResticExecutableSource.Custom,
                CustomExecutablePath = customPath
            };

            FileNotFoundException exception = AssertThrows<FileNotFoundException>(
                () => locator.Resolve(config));

            Assert.AreEqual(customPath, exception.FileName);
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void Resolve_WithCustomSource_WhenPathIsNotConfigured_ShouldThrowInvalidOperationException(string? customPath)
        {
            var locator = new ResticExecutableLocator(_baseDirectory);
            var config = new ResticConfig
            {
                ExecutableSource = ResticExecutableSource.Custom,
                CustomExecutablePath = customPath
            };

            AssertThrows<InvalidOperationException>(() => locator.Resolve(config));
        }

        // ============================================================
        // CASOS RAROS
        // ============================================================

        [TestMethod]
        public void Resolve_WithNullConfig_ShouldThrowArgumentNullException()
        {
            var locator = new ResticExecutableLocator(_baseDirectory);

            AssertThrows<ArgumentNullException>(() => locator.Resolve(null!));
        }

        [TestMethod]
        public void Resolve_WithInvalidExecutableSource_ShouldThrowInvalidOperationException()
        {
            var locator = new ResticExecutableLocator(_baseDirectory);
            var config = new ResticConfig { ExecutableSource = (ResticExecutableSource)99 };

            AssertThrows<InvalidOperationException>(() => locator.Resolve(config));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        private static void CreateEmptyFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            File.WriteAllBytes(path, Array.Empty<byte>());
        }

        private static TException AssertThrows<TException>(Action action)
            where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException exception)
            {
                return exception;
            }
            catch (Exception exception)
            {
                Assert.Fail(
                    $"Se esperaba {typeof(TException).Name}, " +
                    $"pero se produjo {exception.GetType().Name}.");
            }

            Assert.Fail($"Se esperaba una excepción de tipo {typeof(TException).Name}.");

            return null!;
        }
    }
}