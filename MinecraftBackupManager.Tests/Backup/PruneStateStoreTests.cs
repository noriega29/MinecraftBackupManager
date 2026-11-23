using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;

namespace MinecraftBackupManager.Tests.Backup
{
    [TestClass]
    public sealed class PruneStateStoreTests
    {
        private string _root = string.Empty;
        private ApplicationPaths _paths = null!;
        private FakeTimeProvider _clock = null!;

        [TestInitialize]
        public void Setup()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            _paths = new ApplicationPaths(Path.Combine(_root, "Data"));
            _clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero));
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
        // SIN ARCHIVO
        // ============================================================

        [TestMethod]
        public void ShouldPrune_WhenFileDoesNotExist_ShouldReturnTrueWithoutWarningOrFile()
        {
            PruneStateStore store = CreateStore();

            Assert.IsTrue(store.ShouldPrune());

            // Un archivo inexistente es lo normal: no se avisa ni se crea nada.
            Assert.DoesNotContain("prune-state.json", ReadBackupLog());
            Assert.IsFalse(File.Exists(_paths.PruneStateFile));
        }

        // ============================================================
        // ÉXITO
        // ============================================================

        [TestMethod]
        public void RecordPruneSuccess_ShouldWriteTodayAndStopPruningToday()
        {
            PruneStateStore store = CreateStore();

            store.RecordPruneSuccess();

            Assert.IsFalse(store.ShouldPrune());

            string json = ReadStateFile();

            StringAssert.Contains(json, "\"schemaVersion\": 1");
            StringAssert.Contains(json, "\"lastSuccessDate\": \"2026-11-23\"");
            StringAssert.Contains(json, "\"failureDate\": null");
            StringAssert.Contains(json, "\"failureCount\": 0");
        }

        [TestMethod]
        public void ShouldPrune_TheDayAfterASuccess_ShouldReturnTrue()
        {
            PruneStateStore store = CreateStore();

            store.RecordPruneSuccess();

            _clock.AdvanceDays(1);

            Assert.IsTrue(store.ShouldPrune());
        }

        // ============================================================
        // FALLOS Y REINTENTOS
        // ============================================================

        [TestMethod]
        public void RecordPruneFailure_WithDefaultRetries_ShouldBlockTodayButNotTomorrow()
        {
            PruneStateStore store = CreateStore();

            Assert.IsTrue(store.ShouldPrune());

            store.RecordPruneFailure();

            Assert.IsFalse(store.ShouldPrune());

            _clock.AdvanceDays(1);

            Assert.IsTrue(store.ShouldPrune());
        }

        [TestMethod]
        public void RecordPruneFailure_ShouldKeepTheLastSuccessDate()
        {
            PruneStateStore store = CreateStore();

            store.RecordPruneSuccess();

            _clock.AdvanceDays(1);

            store.RecordPruneFailure();

            string json = ReadStateFile();

            StringAssert.Contains(json, "\"lastSuccessDate\": \"2026-11-23\"");
            StringAssert.Contains(json, "\"failureDate\": \"2026-11-24\"");
            StringAssert.Contains(json, "\"failureCount\": 1");

            Assert.IsFalse(store.ShouldPrune());
        }

        [TestMethod]
        public void ShouldPrune_WithTwoExtraRetries_ShouldAllowThreeAttemptsPerDay()
        {
            PruneStateStore store = CreateStore(retriesPerDay: 2);

            Assert.IsTrue(store.ShouldPrune());

            store.RecordPruneFailure();
            Assert.IsTrue(store.ShouldPrune(), "Tras 1 fallo todavía toca.");

            store.RecordPruneFailure();
            Assert.IsTrue(store.ShouldPrune(), "Tras 2 fallos todavía toca.");

            store.RecordPruneFailure();
            Assert.IsFalse(store.ShouldPrune(), "Tras 3 fallos ya no toca.");
        }

        [TestMethod]
        public void RecordPruneSuccess_AfterAFailureTheSameDay_ShouldClearTheFailures()
        {
            PruneStateStore store = CreateStore(retriesPerDay: 1);

            store.RecordPruneFailure();

            Assert.IsTrue(store.ShouldPrune());

            store.RecordPruneSuccess();

            string json = ReadStateFile();

            StringAssert.Contains(json, "\"lastSuccessDate\": \"2026-11-23\"");
            StringAssert.Contains(json, "\"failureDate\": null");
            StringAssert.Contains(json, "\"failureCount\": 0");

            Assert.IsFalse(store.ShouldPrune());
        }

        [TestMethod]
        public void RecordPruneFailure_OnANewDay_ShouldRestartTheCounterAtOne()
        {
            PruneStateStore store = CreateStore(retriesPerDay: 5);

            store.RecordPruneFailure();
            store.RecordPruneFailure();

            StringAssert.Contains(ReadStateFile(), "\"failureCount\": 2");

            _clock.AdvanceDays(1);

            store.RecordPruneFailure();

            string json = ReadStateFile();

            StringAssert.Contains(json, "\"failureDate\": \"2026-11-24\"");
            StringAssert.Contains(json, "\"failureCount\": 1");
        }

        // ============================================================
        // ARCHIVO DAÑADO
        // ============================================================

        [TestMethod]
        [DataRow("")]
        [DataRow("{\"schemaVersion\": 1, \"lastSuccessDate\": \"2026-11-2")]
        [DataRow("esto no es JSON")]
        [DataRow("null")]
        [DataRow("{\"schemaVersion\": 99, \"lastSuccessDate\": \"2026-11-23\"}")]
        [DataRow("{\"schemaVersion\": 1, \"lastSuccessDate\": \"ayer\"}")]
        [DataRow("{\"schemaVersion\": 1, \"failureDate\": \"2026-11-23\", \"failureCount\": -1}")]
        public void ShouldPrune_WithInvalidFile_ShouldReturnTrueAndLogWarning(string content)
        {
            WriteStateFile(content);

            PruneStateStore store = CreateStore();

            Assert.IsTrue(store.ShouldPrune());

            string log = ReadBackupLog();

            StringAssert.Contains(log, "[Warning]");
            StringAssert.Contains(log, "prune-state.json");
        }

        [TestMethod]
        public void RecordPruneSuccess_OverDamagedFile_ShouldRepairIt()
        {
            WriteStateFile("{ roto");

            PruneStateStore store = CreateStore();

            store.RecordPruneSuccess();

            StringAssert.Contains(ReadStateFile(), "\"lastSuccessDate\": \"2026-11-23\"");

            // Ya es un archivo sano: otra instancia lo lee sin problemas.
            Assert.IsFalse(CreateStore().ShouldPrune());
        }

        [TestMethod]
        public void RecordPruneFailure_OverDamagedFile_ShouldStartFromEmptyState()
        {
            WriteStateFile("esto no es JSON");

            PruneStateStore store = CreateStore();

            store.RecordPruneFailure();

            string json = ReadStateFile();

            StringAssert.Contains(json, "\"lastSuccessDate\": null");
            StringAssert.Contains(json, "\"failureDate\": \"2026-11-23\"");
            StringAssert.Contains(json, "\"failureCount\": 1");
        }

        // ============================================================
        // FECHAS RARAS Y ERRORES DE DISCO
        // ============================================================

        [TestMethod]
        public void ShouldPrune_WithFutureSuccessDate_ShouldReturnTrueAndNextSuccessOverwritesIt()
        {
            WriteStateFile("{\"schemaVersion\": 1, \"lastSuccessDate\": \"2026-12-05\", \"failureDate\": null, \"failureCount\": 0}");

            PruneStateStore store = CreateStore();

            // El reloj se atrasó: una fecha futura no cuenta como "hoy".
            Assert.IsTrue(store.ShouldPrune());

            store.RecordPruneSuccess();

            string json = ReadStateFile();

            StringAssert.Contains(json, "\"lastSuccessDate\": \"2026-11-23\"");
            Assert.DoesNotContain("2026-12-05", json);
        }

        [TestMethod]
        public void ShouldPrune_WhenFileCannotBeRead_ShouldThrowAndKeepTheFile()
        {
            const string original = "{\"schemaVersion\": 1, \"lastSuccessDate\": \"2026-11-22\", \"failureDate\": null, \"failureCount\": 0}";

            WriteStateFile(original);

            PruneStateStore store = CreateStore();

            using (new FileStream(_paths.PruneStateFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // Un archivo que no se puede leer puede ser válido: no se trata como dañado.
                TestAssert.Throws<IOException>(() => store.ShouldPrune());
            }

            Assert.AreEqual(original, ReadStateFile());
        }

        // ============================================================
        // ESCRITURA, REINICIOS Y RELOJ
        // ============================================================

        [TestMethod]
        public void Save_ShouldNotLeaveTemporaryFile()
        {
            PruneStateStore store = CreateStore();

            store.RecordPruneSuccess();
            store.RecordPruneFailure();

            Assert.IsTrue(File.Exists(_paths.PruneStateFile));
            Assert.IsFalse(File.Exists(_paths.PruneStateFile + ".tmp"));
        }

        [TestMethod]
        public void State_ShouldSurviveAnApplicationRestart()
        {
            CreateStore().RecordPruneFailure();

            // Otra instancia, como al reiniciar la aplicación.
            Assert.IsFalse(CreateStore().ShouldPrune());
        }

        [TestMethod]
        public void ShouldPrune_ShouldUseTheLocalDayAndNotTheUtcDay()
        {
            _clock.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("Test UTC-5", TimeSpan.FromHours(-5), "Test", "Test"));

            // 03:00 UTC del 24 son las 22:00 del 23 en esa zona.
            _clock.SetUtcNow(new DateTimeOffset(2026, 11, 24, 3, 0, 0, TimeSpan.Zero));

            PruneStateStore store = CreateStore();

            store.RecordPruneSuccess();

            StringAssert.Contains(ReadStateFile(), "\"lastSuccessDate\": \"2026-11-23\"");
            Assert.IsFalse(store.ShouldPrune());

            // 06:00 UTC del 24 son las 01:00 del 24 en esa zona: ya es otro día local.
            _clock.SetUtcNow(new DateTimeOffset(2026, 11, 24, 6, 0, 0, TimeSpan.Zero));

            Assert.IsTrue(store.ShouldPrune());
        }

        // ============================================================
        // VALIDACIÓN DEL CONSTRUCTOR
        // ============================================================

        [TestMethod]
        public void Constructor_WithNullArguments_ShouldThrowArgumentNullException()
        {
            var logManager = new LogManager(_paths);

            TestAssert.Throws<ArgumentNullException>(() => new PruneStateStore(null!, logManager));
            TestAssert.Throws<ArgumentNullException>(() => new PruneStateStore(_paths, null!));
        }

        [TestMethod]
        public void Constructor_WithNegativeRetries_ShouldThrowArgumentOutOfRangeException()
        {
            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new PruneStateStore(_paths, new LogManager(_paths), _clock, retriesPerDay: -1));
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        private PruneStateStore CreateStore(int retriesPerDay = PruneStateStore.DefaultPruneRetriesPerDay)
        {
            return new PruneStateStore(_paths, new LogManager(_paths), _clock, retriesPerDay);
        }

        private string ReadStateFile()
        {
            return File.ReadAllText(_paths.PruneStateFile);
        }

        private void WriteStateFile(string content)
        {
            _paths.EnsureDirectoriesExist();

            File.WriteAllText(_paths.PruneStateFile, content);
        }

        private string ReadBackupLog()
        {
            string path = Path.Combine(_paths.LogsDirectory, "backup.log");

            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }
    }
}
