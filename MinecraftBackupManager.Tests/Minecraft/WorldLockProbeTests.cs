using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Minecraft
{
    [TestClass]
    public sealed class WorldLockProbeTests
    {
        // Los tres bytes de "☃", lo que escribe Minecraft en session.lock.
        private static readonly byte[] LockContent = { 0xE2, 0x98, 0x83 };

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

        // ============================================================
        // RESULTADOS
        // ============================================================

        [TestMethod]
        public void CheckWorldLock_WhenNobodyHoldsTheLock_ShouldReturnFree()
        {
            string serverDirectory = CreateServer("Test Server", "world");

            WriteSessionLock(serverDirectory, "world");

            Assert.AreEqual(SessionLockState.Free, CreateProbe(serverDirectory).CheckWorldLock());
        }

        [TestMethod]
        public void CheckWorldLock_WhenSessionLockDoesNotExist_ShouldReturnFree()
        {
            string serverDirectory = CreateServer("Test Server", "world");

            Assert.AreEqual(SessionLockState.Free, CreateProbe(serverDirectory).CheckWorldLock());
        }

        [TestMethod]
        public void CheckWorldLock_WhenServerHoldsTheLock_ShouldReturnInUse()
        {
            string serverDirectory = CreateServer("Test Server", "world");
            string lockPath = WriteSessionLock(serverDirectory, "world");

            // Imita al servidor: abre el archivo en modo compartido y bloquea su contenido.
            using var holder = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            holder.Lock(0, long.MaxValue);

            Assert.AreEqual(SessionLockState.InUse, CreateProbe(serverDirectory).CheckWorldLock());
        }

        [TestMethod]
        public void CheckWorldLock_ShouldUseTheCurrentServerDirectoryOfTheConfiguration()
        {
            string first = CreateServer("Server A", "world");
            string second = CreateServer("Server B", "world");

            WriteSessionLock(first, "world");
            string secondLock = WriteSessionLock(second, "world");

            // Solo el segundo servidor tiene el bloqueo.
            using var holder = new FileStream(secondLock, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            holder.Lock(0, long.MaxValue);

            var config = new MinecraftServerConfig { ServerDirectory = first };
            var probe = new WorldLockProbe(config);

            Assert.AreEqual(SessionLockState.Free, probe.CheckWorldLock());

            // Se cambia la configuración: la siguiente consulta mira el otro servidor.
            config.ServerDirectory = second;

            Assert.AreEqual(SessionLockState.InUse, probe.CheckWorldLock());
        }

        // ============================================================
        // ERRORES: LLEGAN SIN DISFRAZARSE
        // ============================================================

        [TestMethod]
        public void CheckWorldLock_WhenServerPropertiesDoesNotExist_ShouldThrowFileNotFoundException()
        {
            string serverDirectory = Path.Combine(_root, "Test Server");

            Directory.CreateDirectory(Path.Combine(serverDirectory, "world"));

            TestAssert.Throws<FileNotFoundException>(() => CreateProbe(serverDirectory).CheckWorldLock());
        }

        [TestMethod]
        public void CheckWorldLock_WhenWorldFolderDoesNotExist_ShouldThrowDirectoryNotFoundException()
        {
            string serverDirectory = Path.Combine(_root, "Test Server");

            Directory.CreateDirectory(serverDirectory);
            File.WriteAllText(Path.Combine(serverDirectory, "server.properties"), "level-name=Mundo\n");

            TestAssert.Throws<DirectoryNotFoundException>(() => CreateProbe(serverDirectory).CheckWorldLock());
        }

        [TestMethod]
        public void CheckWorldLock_WhenLevelNameEscapesServerDirectory_ShouldThrowInvalidDataException()
        {
            string serverDirectory = Path.Combine(_root, "Test Server");

            Directory.CreateDirectory(serverDirectory);
            File.WriteAllText(Path.Combine(serverDirectory, "server.properties"), "level-name=../outside\n");

            TestAssert.Throws<InvalidDataException>(() => CreateProbe(serverDirectory).CheckWorldLock());
        }

        // ============================================================
        // VALIDACIÓN
        // ============================================================

        [TestMethod]
        public void Constructor_WithNullArguments_ShouldThrowArgumentNullException()
        {
            var config = new MinecraftServerConfig();

            TestAssert.Throws<ArgumentNullException>(() => new WorldLockProbe(null!));
            TestAssert.Throws<ArgumentNullException>(() => new WorldLockProbe(config, null!, new SessionLockDetector()));
            TestAssert.Throws<ArgumentNullException>(() => new WorldLockProbe(config, new SessionLockLocator(), null!));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        // Crea la carpeta de un servidor con su server.properties y la carpeta del mundo.
        private string CreateServer(string serverName, string levelName)
        {
            string serverDirectory = Path.Combine(_root, serverName);

            Directory.CreateDirectory(Path.Combine(serverDirectory, levelName));
            File.WriteAllText(Path.Combine(serverDirectory, "server.properties"), $"level-name={levelName}\n");

            return serverDirectory;
        }

        private static string WriteSessionLock(string serverDirectory, string levelName)
        {
            string path = Path.Combine(serverDirectory, levelName, "session.lock");

            File.WriteAllBytes(path, LockContent);

            return path;
        }

        private static WorldLockProbe CreateProbe(string serverDirectory)
        {
            return new WorldLockProbe(new MinecraftServerConfig { ServerDirectory = serverDirectory });
        }
    }
}
