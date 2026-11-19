using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Restic
{
    [TestClass]
    public sealed class ResticClientTests
    {
        private const int LargeFileCount = 20;
        private const int LargeFileSizeMb = 10;

        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);

        [TestMethod]
        public async Task GetVersionAsync_ShouldReturnResticVersionLine()
        {
            using var context = new ResticTestContext();

            string version = await context.Client.GetVersionAsync().WaitAsync(TestTimeout);

            StringAssert.StartsWith(version, "restic ");
            Assert.DoesNotContain('\n', version, "Debería ser una sola línea.");
        }

        [TestMethod]
        public async Task GetVersionAsync_WithMissingCustomExecutable_ShouldThrowFileNotFoundException()
        {
            using var context = new ResticTestContext();

            // La carpeta no existe, así que el ejecutable tampoco.
            string missingPath = Path.Combine(context.RepositoryPath, "restic.exe");

            ResticClient client = context.CreateClient(new ResticConfig
            {
                ExecutableSource = ResticExecutableSource.Custom,
                CustomExecutablePath = missingPath,
                RepositoryPath = context.RepositoryPath
            });

            await TestAssert.ThrowsAsync<FileNotFoundException>(
                () => client.GetVersionAsync().WaitAsync(TestTimeout));
        }

        // ============================================================
        // ESTADO DEL REPOSITORIO
        // ============================================================

        [TestMethod]
        public async Task GetRepositoryStateAsync_WhenRepositoryDoesNotExist_ShouldReturnNotInitialized()
        {
            using var context = new ResticTestContext();

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.NotInitialized, state);
        }

        [TestMethod]
        public async Task GetRepositoryStateAsync_WithEmptyRepositoryPath_ShouldThrowInvalidOperationException()
        {
            using var context = new ResticTestContext();

            ResticClient client = context.CreateClient(new ResticConfig
            {
                ExecutableSource = ResticExecutableSource.Bundled,
                RepositoryPath = string.Empty
            });

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => client.GetRepositoryStateAsync().WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task GetRepositoryStateAsync_WithWrongPassword_ShouldReturnWrongPassword()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.SetRepositoryPassword("another-password");

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.WrongPassword, state);
        }

        // ============================================================
        // CREDENCIALES Y REGISTRO
        // ============================================================

        [TestMethod]
        public async Task GetRepositoryStateAsync_WithoutSavedCredentials_ShouldThrowBeforeRunningRestic()
        {
            using var context = new ResticTestContext();

            context.DeleteCredentials();

            // El repositorio no existe: si Restic llegara a ejecutarse, devolvería
            // NotInitialized en vez de lanzar una excepción.
            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task GetRepositoryStateAsync_WithEmptyPassword_ShouldThrowBeforeRunningRestic()
        {
            using var context = new ResticTestContext();

            context.SetRepositoryPassword(string.Empty);

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task ResticClient_ShouldLogOperationsWithoutExposingPassword()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);
            await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            string log = context.ReadApplicationLog();

            StringAssert.Contains(log, "Repositorio Restic inicializado.");
            StringAssert.Contains(log, "Estado del repositorio Restic: Ready.");
            Assert.DoesNotContain(ResticTestContext.RepositoryPassword, log, "La contraseña no debe aparecer en el log.");
        }

        // ============================================================
        // INICIALIZACIÓN
        // ============================================================

        [TestMethod]
        public async Task InitializeRepositoryAsync_ShouldCreateRepositoryThatIsReady()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            Assert.IsTrue(
                File.Exists(Path.Combine(context.RepositoryPath, "config")),
                "Restic debería haber creado el archivo 'config' del repositorio.");

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.Ready, state);
        }

        [TestMethod]
        public async Task InitializeRepositoryAsync_WhenRepositoryAlreadyExists_ShouldFailWithoutOverwriting()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            string configPath = Path.Combine(context.RepositoryPath, "config");
            byte[] configBefore = File.ReadAllBytes(configPath);

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout));

            CollectionAssert.AreEqual(configBefore, File.ReadAllBytes(configPath));

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.Ready, state);
        }

        // ============================================================
        // RESPALDO
        // ============================================================

        [TestMethod]
        public async Task BackupAsync_ShouldCreateSnapshotAndReturnStatistics()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");
            context.WriteSourceFile(Path.Combine("world", "region", "r.0.0.mca"), "datos de la region");

            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(64, result.SnapshotId.Length);
            Assert.IsFalse(result.IsIncomplete);
            Assert.AreEqual(2, result.FilesNew);
            Assert.IsEmpty(result.Warnings);

            string snapshotsDirectory = Path.Combine(context.RepositoryPath, "snapshots");

            Assert.HasCount(1, Directory.GetFiles(snapshotsDirectory), "Debería haber un snapshot en el repositorio.");
            StringAssert.Contains(context.ReadBackupLog(), "Respaldo completado.");
        }

        [TestMethod]
        public async Task BackupAsync_WhenNothingChanged_ShouldCreateNewSnapshotReusingData()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");
            context.WriteSourceFile(Path.Combine("world", "region", "r.0.0.mca"), "datos de la region");

            ResticBackupResult first = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);
            ResticBackupResult second = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreNotEqual(first.SnapshotId, second.SnapshotId);
            Assert.AreEqual(0, second.FilesNew);
            Assert.AreEqual(0, second.FilesChanged);
            Assert.AreEqual(2, second.FilesUnmodified);

            string snapshotsDirectory = Path.Combine(context.RepositoryPath, "snapshots");

            Assert.HasCount(2, Directory.GetFiles(snapshotsDirectory));
        }

        [TestMethod]
        public async Task BackupAsync_ShouldExcludeSessionLockAtAnyLevel()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");
            context.WriteSourceFile("session.lock", "bloqueo");
            context.WriteSourceFile(Path.Combine("world", "session.lock"), "bloqueo");

            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            // Solo level.dat cuenta como archivo nuevo.
            Assert.AreEqual(1, result.FilesNew);
        }

        [TestMethod]
        public async Task BackupAsync_WithNonExistentSourceDirectory_ShouldThrowDirectoryNotFoundException()
        {
            using var context = new ResticTestContext();

            string missingPath = Path.Combine(context.SourcePath, "no-existe");

            await TestAssert.ThrowsAsync<DirectoryNotFoundException>(
                () => context.Client.BackupAsync(missingPath, ResticBackupKind.Manual).WaitAsync(TestTimeout));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow(@"carpeta\relativa")]
        public async Task BackupAsync_WithInvalidSourcePath_ShouldThrowArgumentException(string? sourcePath)
        {
            using var context = new ResticTestContext();

            await TestAssert.ThrowsAsync<ArgumentException>(
                () => context.Client.BackupAsync(sourcePath!, ResticBackupKind.Manual).WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task BackupAsync_WhenRepositoryIsNotInitialized_ShouldThrowInvalidOperationException()
        {
            using var context = new ResticTestContext();

            context.WriteSourceFile("level.dat", "datos del mundo");

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout));

            StringAssert.Contains(context.ReadBackupLog(), "Falló el respaldo");
        }

        [TestMethod]
        public async Task BackupAsync_WithWrongPassword_ShouldThrowAndCreateNoSnapshot()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");
            context.SetRepositoryPassword("another-password");

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout));

            string snapshotsDirectory = Path.Combine(context.RepositoryPath, "snapshots");

            Assert.IsEmpty(Directory.GetFiles(snapshotsDirectory));
        }

        [TestMethod]
        public async Task BackupAsync_WithUndefinedKind_ShouldThrowBeforeRunningRestic()
        {
            using var context = new ResticTestContext();

            // El repositorio no está inicializado: si Restic llegara a ejecutarse,
            // el error sería otro (InvalidOperationException).
            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => context.Client.BackupAsync(context.SourcePath, (ResticBackupKind)99).WaitAsync(TestTimeout));
        }

        // ============================================================
        // ARCHIVOS BLOQUEADOS
        // ============================================================

        [TestMethod]
        public async Task BackupAsync_WithLockedFile_ShouldReturnIncompleteResultWithWarning()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            string lockedPath = Path.Combine(context.SourcePath, "bloqueado.dat");
            using FileStream lockedStream = LockFile(lockedPath);

            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.IsTrue(result.IsIncomplete);
            Assert.AreEqual(64, result.SnapshotId.Length);
            Assert.AreEqual(1, result.FilesNew, "Solo level.dat debería haberse respaldado.");
            Assert.HasCount(1, result.Warnings);
            Assert.AreEqual(lockedPath, result.Warnings[0].Item, ignoreCase: true);

            // El snapshot se crea igualmente.
            string snapshotsDirectory = Path.Combine(context.RepositoryPath, "snapshots");

            Assert.HasCount(1, Directory.GetFiles(snapshotsDirectory));

            string log = context.ReadBackupLog();

            StringAssert.Contains(log, "Respaldo incompleto");
            StringAssert.Contains(log, "bloqueado.dat");
        }

        [TestMethod]
        public async Task BackupAsync_WithLockedSessionLock_ShouldStillBeComplete()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            // El caso real: session.lock dentro de la carpeta del mundo, bloqueado por el servidor.
            using FileStream lockedStream = LockFile(Path.Combine(context.SourcePath, "world", "session.lock"));

            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.IsFalse(result.IsIncomplete);
            Assert.IsEmpty(result.Warnings);
            Assert.AreEqual(1, result.FilesNew);
        }

        [TestMethod]
        public async Task BackupAsync_AfterLockedFileIsReleased_ShouldIncludeItAndBeComplete()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            using FileStream lockedStream = LockFile(Path.Combine(context.SourcePath, "bloqueado.dat"));

            ResticBackupResult first = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.IsTrue(first.IsIncomplete);

            lockedStream.Dispose();

            ResticBackupResult second = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.IsFalse(second.IsIncomplete);
            Assert.IsEmpty(second.Warnings);
            Assert.AreEqual(1, second.FilesNew, "bloqueado.dat debería entrar ahora.");
            Assert.AreEqual(1, second.FilesUnmodified);
        }

        // ============================================================
        // CANCELACIÓN
        // ============================================================

        [TestMethod]
        [DoNotParallelize]
        public async Task BackupAsync_WhenCanceledMidBackup_ShouldThrowLogAndLeaveNoProcessRunning()
        {
            HashSet<int> resticBefore = GetRunningResticProcessIds();

            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            await CancelBackupMidwayAsync(context);

            StringAssert.Contains(context.ReadBackupLog(), "Respaldo cancelado.");

            string locksDirectory = Path.Combine(context.RepositoryPath, "locks");

            Console.WriteLine($"Archivos en 'locks' después de cancelar: {Directory.EnumerateFiles(locksDirectory).Count()}");

            bool noProcessesLeft = await WaitUntilAsync(
                () => !GetRunningResticProcessIds().Except(resticBefore).Any(),
                TimeSpan.FromSeconds(2));

            if (!noProcessesLeft)
            {
                Assert.Fail(DescribeLeftoverResticProcesses(resticBefore));
            }
        }

        [TestMethod]
        public async Task BackupAsync_AfterCancellation_ShouldWorkAgainWithUsableRepository()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            await CancelBackupMidwayAsync(context);

            // Se quitan los archivos grandes para que la segunda vuelta sea rápida.
            Directory.Delete(Path.Combine(context.SourcePath, "big"), recursive: true);

            context.WriteSourceFile("level.dat", "datos del mundo");

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.Ready, state);

            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.IsFalse(result.IsIncomplete);
            Assert.AreEqual(1, result.FilesNew);

            // El respaldo cancelado no dejó ningún snapshot; solo existe el nuevo.
            string snapshotsDirectory = Path.Combine(context.RepositoryPath, "snapshots");

            Assert.HasCount(1, Directory.GetFiles(snapshotsDirectory));
        }

        // ============================================================
        // LISTADO DE SNAPSHOTS
        // ============================================================

        [TestMethod]
        public async Task ListSnapshotsAsync_OnNewRepository_ShouldReturnEmptyList()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            IReadOnlyList<ResticSnapshot> snapshots = await context.Client.ListSnapshotsAsync().WaitAsync(TestTimeout);

            Assert.IsEmpty(snapshots);
        }

        [TestMethod]
        public async Task ListSnapshotsAsync_ShouldReturnEverySnapshotWithItsKindTag()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            ResticBackupKind[] kinds = Enum.GetValues<ResticBackupKind>();
            var expectedIds = new List<string>();

            foreach (ResticBackupKind kind in kinds)
            {
                ResticBackupResult backup = await context.Client.BackupAsync(context.SourcePath, kind).WaitAsync(TestTimeout);

                expectedIds.Add(backup.SnapshotId);
            }

            IReadOnlyList<ResticSnapshot> snapshots = await context.Client.ListSnapshotsAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(expectedIds, snapshots.Select(snapshot => snapshot.Id).ToList());

            for (int index = 0; index < kinds.Length; index++)
            {
                ResticSnapshot snapshot = snapshots[index];

                Assert.HasCount(1, snapshot.Tags);
                Assert.AreEqual(kinds[index].ToTag(), snapshot.Tags[0]);
                Assert.AreEqual("minecraft-backup-manager", snapshot.Host);
                Assert.HasCount(1, snapshot.Paths);
                Assert.AreEqual(context.SourcePath, snapshot.Paths[0], ignoreCase: true);
            }
        }

        [TestMethod]
        public async Task ListSnapshotsAsync_WithKindFilter_ShouldReturnOnlyThatKind()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            ResticBackupResult firstHourly = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Hourly).WaitAsync(TestTimeout);
            await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Daily).WaitAsync(TestTimeout);
            ResticBackupResult secondHourly = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Hourly).WaitAsync(TestTimeout);
            await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            IReadOnlyList<ResticSnapshot> hourly = await context.Client.ListSnapshotsAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { firstHourly.SnapshotId, secondHourly.SnapshotId },
                hourly.Select(snapshot => snapshot.Id).ToList());

            IReadOnlyList<ResticSnapshot> weekly = await context.Client.ListSnapshotsAsync(ResticBackupKind.Weekly).WaitAsync(TestTimeout);

            Assert.IsEmpty(weekly);
        }

        [TestMethod]
        public async Task ListSnapshotsAsync_WhenRepositoryIsNotInitialized_ShouldThrowInvalidOperationException()
        {
            using var context = new ResticTestContext();

            await TestAssert.ThrowsAsync<InvalidOperationException>(
                () => context.Client.ListSnapshotsAsync().WaitAsync(TestTimeout));
        }

        // ============================================================
        // BLOQUEOS, RETENCIÓN Y COMPACTACIÓN
        // ============================================================

        [TestMethod]
        public async Task UnlockAsync_OnRepositoryWithoutLocks_ShouldSucceed()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);
            await context.Client.UnlockAsync().WaitAsync(TestTimeout);

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.Ready, state);
        }

        [TestMethod]
        public async Task UnlockAsync_AfterCanceledBackup_ShouldRemoveLeftoverLock()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            await CancelBackupMidwayAsync(context);

            string locksDirectory = Path.Combine(context.RepositoryPath, "locks");

            Assert.HasCount(1, Directory.GetFiles(locksDirectory), "El corte debería haber dejado un bloqueo.");

            await context.Client.UnlockAsync().WaitAsync(TestTimeout);

            Assert.IsEmpty(Directory.GetFiles(locksDirectory), "unlock debería haber eliminado el bloqueo huérfano.");
        }

        [TestMethod]
        public async Task ForgetAsync_ShouldKeepOnlyLastCopiesOfThatKindAndTouchNoOtherKind()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);
            string hourly2 = await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);
            string hourly3 = await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);
            string daily = await BackupAndGetIdAsync(context, ResticBackupKind.Daily);
            string initial = await BackupAndGetIdAsync(context, ResticBackupKind.Initial);
            string manual = await BackupAndGetIdAsync(context, ResticBackupKind.Manual);

            await context.Client.ForgetAsync(ResticBackupKind.Hourly, keepLast: 2).WaitAsync(TestTimeout);

            IReadOnlyList<ResticSnapshot> snapshots = await context.Client.ListSnapshotsAsync().WaitAsync(TestTimeout);

            // Desaparece solo la hourly más antigua.
            CollectionAssert.AreEqual(
                new[] { hourly2, hourly3, daily, initial, manual },
                snapshots.Select(snapshot => snapshot.Id).ToList());
        }

        [TestMethod]
        [DataRow(ResticBackupKind.Initial)]
        [DataRow(ResticBackupKind.Manual)]
        public async Task ForgetAsync_WithKindThatMustNeverBeDeleted_ShouldThrowBeforeRunningRestic(ResticBackupKind kind)
        {
            using var context = new ResticTestContext();

            // El repositorio no está inicializado: si Restic llegara a ejecutarse,
            // el error sería otro (InvalidOperationException).
            await TestAssert.ThrowsAsync<ArgumentException>(
                () => context.Client.ForgetAsync(kind, keepLast: 5).WaitAsync(TestTimeout));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public async Task ForgetAsync_WithKeepLastBelowOne_ShouldThrowBeforeRunningRestic(int keepLast)
        {
            using var context = new ResticTestContext();

            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => context.Client.ForgetAsync(ResticBackupKind.Hourly, keepLast).WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task ForgetAsync_WithUndefinedKind_ShouldThrowArgumentOutOfRangeException()
        {
            using var context = new ResticTestContext();

            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => context.Client.ForgetAsync((ResticBackupKind)99, keepLast: 5).WaitAsync(TestTimeout));
        }

        [TestMethod]
        public async Task ForgetAsync_AfterCanceledBackup_ShouldSucceedDespiteLeftoverLock()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);
            string hourly2 = await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);

            // Un respaldo grande cancelado a mitad deja un bloqueo huérfano en el repositorio.
            await CancelBackupMidwayAsync(context);

            await context.Client.ForgetAsync(ResticBackupKind.Hourly, keepLast: 1).WaitAsync(TestTimeout);

            IReadOnlyList<ResticSnapshot> snapshots = await context.Client.ListSnapshotsAsync().WaitAsync(TestTimeout);

            // El respaldo cancelado no dejó ningún snapshot, y solo queda la hourly más reciente.
            CollectionAssert.AreEqual(new[] { hourly2 }, snapshots.Select(snapshot => snapshot.Id).ToList());
        }

        [TestMethod]
        public async Task PruneAsync_AfterForget_ShouldLeaveRemainingSnapshotsUsable()
        {
            using var context = new ResticTestContext();

            await context.Client.InitializeRepositoryAsync().WaitAsync(TestTimeout);

            context.WriteSourceFile("level.dat", "datos del mundo");

            await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);
            await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);
            string hourly3 = await BackupAndGetIdAsync(context, ResticBackupKind.Hourly);

            await context.Client.ForgetAsync(ResticBackupKind.Hourly, keepLast: 1).WaitAsync(TestTimeout);
            await context.Client.PruneAsync().WaitAsync(TestTimeout);

            IReadOnlyList<ResticSnapshot> snapshots = await context.Client.ListSnapshotsAsync().WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(new[] { hourly3 }, snapshots.Select(snapshot => snapshot.Id).ToList());

            ResticRepositoryState state = await context.Client.GetRepositoryStateAsync().WaitAsync(TestTimeout);

            Assert.AreEqual(ResticRepositoryState.Ready, state);

            // Se puede seguir respaldando después de compactar.
            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.IsFalse(result.IsIncomplete);
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        // Abre el archivo en modo exclusivo: mientras el flujo exista, nadie más puede leerlo.
        private static FileStream LockFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            stream.WriteByte(65);
            stream.Flush();

            return stream;
        }

        // Lanza un respaldo grande, espera a que Restic esté trabajando y lo cancela.
        private static async Task CancelBackupMidwayAsync(ResticTestContext context)
        {
            using var cts = new CancellationTokenSource();

            CreateLargeSource(context);

            Task<ResticBackupResult> backupTask = context.Client.BackupAsync(context.SourcePath, ResticBackupKind.Manual, cts.Token);

            string locksDirectory = Path.Combine(context.RepositoryPath, "locks");

            // Restic crea un archivo de bloqueo en "locks" en cuanto abre el repositorio para respaldar.
            bool started = await WaitUntilAsync(
                () => backupTask.IsCompleted
                    || (Directory.Exists(locksDirectory) && Directory.EnumerateFiles(locksDirectory).Any()),
                TimeSpan.FromSeconds(10));

            Assert.IsTrue(
                started,
                "No apareció ningún bloqueo en 'locks' en 10 segundos: Restic no llegó a empezar, o no usa bloqueos como se supuso.");

            if (backupTask.IsCompleted)
            {
                // Si falló, esto lanza el error real; si terminó bien, la prueba no puede continuar.
                await backupTask;

                Assert.Inconclusive("El respaldo terminó antes de poder cancelarlo. Aumenta LargeFileCount o LargeFileSizeMb.");
            }

            await cts.CancelAsync();

            try
            {
                await backupTask.WaitAsync(TestTimeout);
            }
            catch (OperationCanceledException)
            {
                return; // Es lo esperado.
            }

            Assert.Inconclusive("El respaldo terminó antes de que la cancelación surtiera efecto. Aumenta LargeFileCount o LargeFileSizeMb.");
        }

        private static void CreateLargeSource(ResticTestContext context)
        {
            string directory = Path.Combine(context.SourcePath, "big");

            Directory.CreateDirectory(directory);

            byte[] buffer = new byte[1024 * 1024];

            for (int file = 0; file < LargeFileCount; file++)
            {
                using var stream = new FileStream(Path.Combine(directory, $"file{file}.bin"), FileMode.Create, FileAccess.Write);

                for (int megabyte = 0; megabyte < LargeFileSizeMb; megabyte++)
                {
                    RandomNumberGenerator.Fill(buffer);
                    stream.Write(buffer);
                }
            }
        }

        private static HashSet<int> GetRunningResticProcessIds()
        {
            Process[] processes = Process.GetProcessesByName("restic");

            try
            {
                return processes.Where(IsAlive).Select(process => process.Id).ToHashSet();
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static bool IsAlive(Process process)
        {
            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false; // El proceso ya no existe.
            }
            catch (Win32Exception)
            {
                return true; // No se puede consultar; se asume que sigue vivo.
            }
        }

        private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < timeout)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(20);
            }

            return condition();
        }

        private static string DescribeLeftoverResticProcesses(HashSet<int> baseline)
        {
            Process[] processes = Process.GetProcessesByName("restic");
            var lines = new List<string>();

            try
            {
                foreach (Process process in processes.Where(process => !baseline.Contains(process.Id)))
                {
                    string state;
                    string start;

                    try
                    {
                        state = process.HasExited ? "ya había terminado" : "sigue en ejecución";
                    }
                    catch (Exception exception)
                    {
                        state = $"estado desconocido ({exception.GetType().Name})";
                    }

                    try
                    {
                        start = process.StartTime.ToString("HH:mm:ss.fff");
                    }
                    catch (Exception exception)
                    {
                        start = $"desconocido ({exception.GetType().Name})";
                    }

                    lines.Add($"PID {process.Id}: {state}, iniciado a las {start}");
                }
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }

            return $"Quedó un proceso restic en marcha después de cancelar (ahora son las {DateTime.Now:HH:mm:ss.fff}). " +
                string.Join("; ", lines);
        }

        private static async Task<string> BackupAndGetIdAsync(ResticTestContext context, ResticBackupKind kind)
        {
            ResticBackupResult result = await context.Client.BackupAsync(context.SourcePath, kind).WaitAsync(TestTimeout);

            return result.SnapshotId;
        }
    }
}
