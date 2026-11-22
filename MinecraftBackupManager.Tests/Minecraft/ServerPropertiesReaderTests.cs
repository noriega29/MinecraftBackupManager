using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Minecraft
{
    [TestClass]
    public sealed class ServerPropertiesReaderTests
    {
        private readonly ServerPropertiesReader _reader = new();
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
        // LECTURA NORMAL
        // ============================================================

        [TestMethod]
        public void ReadLevelName_WithTypicalFile_ShouldReturnConfiguredValue()
        {
            WriteServerProperties(
                "#Minecraft server properties\n" +
                "#Fri Nov 20 15:30:00 COT 2026\n" +
                "enable-rcon=true\n" +
                "level-name=Mi Mundo\n" +
                "motd=A Minecraft Server\n");

            Assert.AreEqual("Mi Mundo", _reader.ReadLevelName(_serverDirectory));
        }

        [TestMethod]
        [DataRow("level-name=Mundo")]
        [DataRow("level-name = Mundo")]
        [DataRow("  level-name=Mundo")]
        [DataRow("level-name:Mundo")]
        [DataRow("level-name Mundo")]
        [DataRow("level-name=Mundo   ")]
        public void ReadLevelName_ShouldAcceptSeparatorsAndSurroundingSpaces(string line)
        {
            WriteServerProperties(line);

            Assert.AreEqual("Mundo", _reader.ReadLevelName(_serverDirectory));
        }

        [TestMethod]
        public void ReadLevelName_WhenFieldAppearsTwice_ShouldReturnTheLastOne()
        {
            WriteServerProperties("level-name=Primero\nlevel-name=Segundo\n");

            Assert.AreEqual("Segundo", _reader.ReadLevelName(_serverDirectory));
        }

        [TestMethod]
        public void ReadLevelName_ShouldIgnoreCommentedLines()
        {
            WriteServerProperties(
                "#level-name=Comentado\n" +
                "!level-name=OtroComentario\n" +
                "level-name=Real\n");

            Assert.AreEqual("Real", _reader.ReadLevelName(_serverDirectory));
        }

        // ============================================================
        // VALOR POR DEFECTO
        // ============================================================

        [TestMethod]
        public void ReadLevelName_WhenFieldIsMissing_ShouldReturnDefault()
        {
            // El único level-name que hay está comentado, así que no cuenta.
            WriteServerProperties("#level-name=Comentado\nmotd=Hola\n");

            Assert.AreEqual("world", _reader.ReadLevelName(_serverDirectory));
        }

        [TestMethod]
        [DataRow("level-name=")]
        [DataRow("level-name=   ")]
        public void ReadLevelName_WhenValueIsEmpty_ShouldReturnDefault(string line)
        {
            WriteServerProperties(line);

            Assert.AreEqual("world", _reader.ReadLevelName(_serverDirectory));
        }

        // ============================================================
        // LETRAS CON TILDE
        // ============================================================

        [TestMethod]
        public void ReadLevelName_WithUnicodeEscape_ShouldConvertIt()
        {
            // Así guarda Minecraft "María" en algunas versiones.
            WriteServerProperties(@"level-name=Mundo de Mar\u00eda");

            Assert.AreEqual("Mundo de Mar\u00eda", _reader.ReadLevelName(_serverDirectory));
        }

        [TestMethod]
        public void ReadLevelName_WithAccentsWrittenDirectly_ShouldReadThem()
        {
            WriteServerProperties("level-name=Año Nuevo");

            Assert.AreEqual("Año Nuevo", _reader.ReadLevelName(_serverDirectory));
        }

        // ============================================================
        // ARCHIVO ABIERTO POR OTRO PROCESO
        // ============================================================

        [TestMethod]
        public void ReadLevelName_WhenFileIsOpenedByAnotherProcess_ShouldStillRead()
        {
            string path = WriteServerProperties("level-name=Mundo");

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

            Assert.AreEqual("Mundo", _reader.ReadLevelName(_serverDirectory));
        }

        // ============================================================
        // ERRORES
        // ============================================================

        [TestMethod]
        public void ReadLevelName_WhenFileDoesNotExist_ShouldThrowFileNotFoundException()
        {
            FileNotFoundException exception = TestAssert.Throws<FileNotFoundException>(
                () => _reader.ReadLevelName(_serverDirectory));

            StringAssert.Contains(exception.Message, "server.properties");
        }

        [TestMethod]
        public void ReadLevelName_WhenDirectoryDoesNotExist_ShouldThrowFileNotFoundException()
        {
            string missingDirectory = Path.Combine(_root, "no-existe");

            TestAssert.Throws<FileNotFoundException>(() => _reader.ReadLevelName(missingDirectory));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(@"carpeta\relativa")]
        public void ReadLevelName_WithInvalidDirectory_ShouldThrowArgumentException(string? serverDirectory)
        {
            TestAssert.Throws<ArgumentException>(() => _reader.ReadLevelName(serverDirectory!));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        private string WriteServerProperties(string content)
        {
            string path = Path.Combine(_serverDirectory, "server.properties");

            File.WriteAllText(path, content);

            return path;
        }
    }
}
