using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Minecraft
{
    [TestClass]
    public sealed class SessionLockLocatorTests
    {
        private readonly SessionLockLocator _locator = new();
        private string _root = string.Empty;
        private string _serverDirectory = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            // Con un espacio a propósito, como en las demás pruebas.
            _serverDirectory = Path.Combine(_root, "Test Server");

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

        // ============================================================
        // RUTAS VÁLIDAS
        // ============================================================

        [TestMethod]
        public void Resolve_WithoutLevelName_ShouldUseDefaultWorldFolder()
        {
            WriteServerProperties("motd=Hola\n");
            CreateFolder("world");

            string result = _locator.Resolve(_serverDirectory);

            Assert.AreEqual(Path.Combine(_serverDirectory, "world", "session.lock"), result, ignoreCase: true);
        }

        [TestMethod]
        public void Resolve_WithConfiguredLevelName_ShouldUseThatFolder()
        {
            WriteServerProperties("level-name=Mi Mundo\n");
            CreateFolder("Mi Mundo");

            string result = _locator.Resolve(_serverDirectory);

            Assert.AreEqual(Path.Combine(_serverDirectory, "Mi Mundo", "session.lock"), result, ignoreCase: true);
        }

        [TestMethod]
        public void Resolve_WithLevelNameInSubfolder_ShouldReturnPathInsideServerDirectory()
        {
            WriteServerProperties("level-name=worlds/survival\n");
            CreateFolder("worlds", "survival");

            string result = _locator.Resolve(_serverDirectory);

            Assert.AreEqual(
                Path.Combine(_serverDirectory, "worlds", "survival", "session.lock"),
                result,
                ignoreCase: true);
        }

        [TestMethod]
        public void Resolve_WhenSessionLockDoesNotExist_ShouldStillReturnItsPath()
        {
            // El localizador solo dice dónde estaría; saber si existe es trabajo del detector.
            WriteServerProperties("level-name=world\n");
            CreateFolder("world");

            string result = _locator.Resolve(_serverDirectory);

            Assert.IsFalse(File.Exists(result));
        }

        // ============================================================
        // RUTAS QUE SE SALEN DE LA CARPETA DEL SERVIDOR
        // ============================================================

        [TestMethod]
        [DataRow("../outside")]
        [DataRow("..")]
        [DataRow("../Test Server Backup")]
        public void Resolve_WhenLevelNameEscapesServerDirectory_ShouldThrowInvalidDataException(string levelName)
        {
            WriteServerProperties($"level-name={levelName}\n");

            InvalidDataException exception = TestAssert.Throws<InvalidDataException>(
                () => _locator.Resolve(_serverDirectory));

            StringAssert.Contains(exception.Message, "fuera de la carpeta del servidor");
        }

        [TestMethod]
        public void Resolve_WhenLevelNameIsAnAbsolutePathOutsideServer_ShouldThrowInvalidDataException()
        {
            string elsewhere = Path.Combine(_root, "elsewhere");

            Directory.CreateDirectory(elsewhere);

            // Barras normales: en este formato la barra invertida es un carácter de escape.
            WriteServerProperties($"level-name={elsewhere.Replace('\\', '/')}\n");

            TestAssert.Throws<InvalidDataException>(() => _locator.Resolve(_serverDirectory));
        }

        // ============================================================
        // ERRORES
        // ============================================================

        [TestMethod]
        public void Resolve_WhenWorldFolderDoesNotExist_ShouldThrowDirectoryNotFoundException()
        {
            WriteServerProperties("level-name=Mundo\n");

            DirectoryNotFoundException exception = TestAssert.Throws<DirectoryNotFoundException>(
                () => _locator.Resolve(_serverDirectory));

            StringAssert.Contains(exception.Message, "Mundo");
        }

        [TestMethod]
        public void Resolve_WhenServerPropertiesDoesNotExist_ShouldThrowFileNotFoundException()
        {
            CreateFolder("world");

            TestAssert.Throws<FileNotFoundException>(() => _locator.Resolve(_serverDirectory));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(@"carpeta\relativa")]
        public void Resolve_WithInvalidServerDirectory_ShouldThrowArgumentException(string? serverDirectory)
        {
            TestAssert.Throws<ArgumentException>(() => _locator.Resolve(serverDirectory!));
        }

        [TestMethod]
        public void Constructor_WithNullReader_ShouldThrowArgumentNullException()
        {
            TestAssert.Throws<ArgumentNullException>(() => new SessionLockLocator(null!));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        private void WriteServerProperties(string content)
        {
            File.WriteAllText(Path.Combine(_serverDirectory, "server.properties"), content);
        }

        private void CreateFolder(params string[] segments)
        {
            Directory.CreateDirectory(Path.Combine(new[] { _serverDirectory }.Concat(segments).ToArray()));
        }
    }
}
