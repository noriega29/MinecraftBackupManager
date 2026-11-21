using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Minecraft
{
    [TestClass]
    public sealed class SessionLockDetectorTests
    {
        // Los tres bytes de "☃", lo que escribe Minecraft en session.lock.
        private static readonly byte[] LockContent = { 0xE2, 0x98, 0x83 };

        private readonly SessionLockDetector _detector = new();
        private string _root = string.Empty;
        private string _lockPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            // Con un espacio a propósito, como en las demás pruebas.
            string worldDirectory = Path.Combine(_root, "Test Server", "world");

            Directory.CreateDirectory(worldDirectory);

            _lockPath = Path.Combine(worldDirectory, "session.lock");
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
        // SERVIDOR APAGADO
        // ============================================================

        [TestMethod]
        public void Check_WhenFileDoesNotExist_ShouldReturnFree()
        {
            Assert.AreEqual(SessionLockState.Free, _detector.Check(_lockPath));
        }

        [TestMethod]
        public void Check_WhenFolderDoesNotExist_ShouldReturnFree()
        {
            string missing = Path.Combine(_root, "no-existe", "session.lock");

            Assert.AreEqual(SessionLockState.Free, _detector.Check(missing));
        }

        [TestMethod]
        public void Check_WhenFileIsNotLocked_ShouldReturnFree()
        {
            File.WriteAllBytes(_lockPath, LockContent);

            Assert.AreEqual(SessionLockState.Free, _detector.Check(_lockPath));
        }

        [TestMethod]
        public void Check_WhenFileIsEmpty_ShouldReturnFree()
        {
            File.WriteAllBytes(_lockPath, Array.Empty<byte>());

            Assert.AreEqual(SessionLockState.Free, _detector.Check(_lockPath));
        }

        // ============================================================
        // SERVIDOR ENCENDIDO
        // ============================================================

        [TestMethod]
        public void Check_WhenContentIsLockedByAnotherHandle_ShouldReturnInUse()
        {
            File.WriteAllBytes(_lockPath, LockContent);

            // Imita al servidor: abre el archivo en modo compartido y bloquea su contenido.
            using var holder = new FileStream(_lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            holder.Lock(0, long.MaxValue);

            Assert.AreEqual(SessionLockState.InUse, _detector.Check(_lockPath));
        }

        [TestMethod]
        public void Check_WhenFileIsOpenedExclusively_ShouldReturnInUse()
        {
            File.WriteAllBytes(_lockPath, LockContent);

            using var holder = new FileStream(_lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            Assert.AreEqual(SessionLockState.InUse, _detector.Check(_lockPath));
        }

        [TestMethod]
        public void Check_AfterLockIsReleased_ShouldReturnFreeAgain()
        {
            File.WriteAllBytes(_lockPath, LockContent);

            using var holder = new FileStream(_lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

            holder.Lock(0, long.MaxValue);

            Assert.AreEqual(SessionLockState.InUse, _detector.Check(_lockPath));

            holder.Unlock(0, long.MaxValue);

            Assert.AreEqual(SessionLockState.Free, _detector.Check(_lockPath));
        }

        // ============================================================
        // EFECTOS SECUNDARIOS Y VALIDACIÓN
        // ============================================================

        [TestMethod]
        public void Check_ShouldNotLeaveTheFileOpen()
        {
            File.WriteAllBytes(_lockPath, LockContent);

            _detector.Check(_lockPath);

            // Si el detector hubiera dejado el archivo abierto, borrarlo fallaría.
            File.Delete(_lockPath);

            Assert.IsFalse(File.Exists(_lockPath));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(@"carpeta\relativa\session.lock")]
        public void Check_WithInvalidPath_ShouldThrowArgumentException(string? path)
        {
            TestAssert.Throws<ArgumentException>(() => _detector.Check(path!));
        }
    }
}
