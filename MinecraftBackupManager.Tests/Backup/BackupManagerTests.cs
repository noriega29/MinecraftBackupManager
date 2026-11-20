using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Backup
{
    [TestClass]
    public sealed class BackupManagerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        // ============================================================
        // CAMINO FELIZ
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenEverythingSucceeds_ShouldCheckRepositoryThenPrepareThenBackupThenResume()
        {
            using var context = new BackupManagerTestContext();

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "repository-state", "prepare", "backup", "resume" },
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
            Assert.AreEqual(ResticBackupKind.Manual, report.Kind);
            Assert.IsTrue(report.AutosaveRestored);
            Assert.AreSame(context.Repository.BackupResult, report.ResticResult);
            Assert.IsFalse(context.Server.IsAutosaveDisabled);
        }

        [TestMethod]
        [DataRow(ResticBackupKind.Initial)]
        [DataRow(ResticBackupKind.Manual)]
        [DataRow(ResticBackupKind.Hourly)]
        [DataRow(ResticBackupKind.Daily)]
        [DataRow(ResticBackupKind.Weekly)]
        public async Task RunBackupAsync_ShouldBackUpServerDirectoryWithRequestedKind(ResticBackupKind kind)
        {
            using var context = new BackupManagerTestContext();

            BackupReport report = await context.Manager.RunBackupAsync(kind).WaitAsync(TestTimeout);

            Assert.AreEqual(context.ServerDirectory, context.Repository.LastBackupSourcePath);
            Assert.AreEqual(kind, context.Repository.LastBackupKind);
            Assert.AreEqual(kind, report.Kind);
        }

        [TestMethod]
        public async Task RunBackupAsync_CalledTwiceInSequence_ShouldCompleteBothBecauseLockIsReleased()
        {
            using var context = new BackupManagerTestContext();

            BackupReport first = await context.Manager.RunBackupAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);
            BackupReport second = await context.Manager.RunBackupAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Completed, first.Status);
            Assert.AreEqual(BackupStatus.Completed, second.Status);

            CollectionAssert.AreEqual(
                new[]
                {
                    "repository-state", "prepare", "backup", "resume",
                    "repository-state", "prepare", "backup", "resume"
                },
                context.Log.Entries.ToArray());
        }

        // ============================================================
        // REPOSITORIO NO LISTO
        // ============================================================

        [TestMethod]
        [DataRow(ResticRepositoryState.NotInitialized, "no está inicializado")]
        [DataRow(ResticRepositoryState.WrongPassword, "contraseña")]
        public async Task RunBackupAsync_WhenRepositoryIsNotReady_ShouldFailWithoutTouchingServer(
            ResticRepositoryState state, string expectedText)
        {
            using var context = new BackupManagerTestContext();

            context.Repository.State = state;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Failed, report.Status);
            Assert.IsNull(report.ResticResult);
            Assert.IsTrue(report.AutosaveRestored);
            StringAssert.Contains(report.Message, expectedText);

            // Solo se consultó el repositorio: el servidor ni se enteró.
            CollectionAssert.AreEqual(
                new[] { "repository-state" },
                context.Log.Entries.ToArray());
        }

        // ============================================================
        // ADVERTENCIAS
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenBackupIsIncomplete_ShouldReturnCompletedWithWarnings()
        {
            using var context = new BackupManagerTestContext();

            context.Repository.BackupResult = FakeBackupRepository.CreateCompleteResult() with
            {
                IsIncomplete = true,
                Warnings = new[] { new ResticBackupWarning(@"C:\Test Server\bloqueado.dat", "archivo en uso") }
            };

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.CompletedWithWarnings, report.Status);
            Assert.IsTrue(report.AutosaveRestored);
            Assert.AreSame(context.Repository.BackupResult, report.ResticResult);
            StringAssert.Contains(report.Message, "1 elemento");
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenAutosaveCannotBeResumed_ShouldReturnCompletedWithWarnings()
        {
            using var context = new BackupManagerTestContext();

            context.Server.ResumeException = new InvalidOperationException("sin conexión");

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.CompletedWithWarnings, report.Status);
            Assert.IsFalse(report.AutosaveRestored);
            Assert.IsNotNull(report.ResticResult);
            StringAssert.Contains(report.Message, "save-on");
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenBackupIsIncompleteAndAutosaveCannotBeResumed_ShouldMentionBothWarnings()
        {
            using var context = new BackupManagerTestContext();

            context.Repository.BackupResult = FakeBackupRepository.CreateCompleteResult() with
            {
                IsIncomplete = true,
                Warnings = new[] { new ResticBackupWarning(@"C:\Test Server\bloqueado.dat", "archivo en uso") }
            };

            context.Server.ResumeException = new InvalidOperationException("sin conexión");

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.CompletedWithWarnings, report.Status);
            Assert.IsFalse(report.AutosaveRestored);
            StringAssert.Contains(report.Message, "elemento");
            StringAssert.Contains(report.Message, "save-on");
        }

        // ============================================================
        // DOS RESPALDOS A LA VEZ
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenAnotherBackupIsRunning_ShouldReturnAlreadyRunningWithoutTouchingAnything()
        {
            using var context = new BackupManagerTestContext();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            // El primer respaldo se queda "trabajando" hasta que se abra la compuerta.
            Task<BackupReport> first = context.Manager.RunBackupAsync(ResticBackupKind.Hourly);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);

            BackupReport second = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.AlreadyRunning, second.Status);
            Assert.AreEqual(ResticBackupKind.Manual, second.Kind);
            Assert.IsNull(second.ResticResult);
            Assert.IsTrue(second.AutosaveRestored);

            // El segundo intento no hizo ninguna llamada: solo aparece lo del primero.
            CollectionAssert.AreEqual(
                new[] { "repository-state", "prepare", "backup" },
                context.Log.Entries.ToArray());

            gate.SetResult();

            BackupReport firstReport = await first.WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Completed, firstReport.Status);
        }

        // ============================================================
        // CANCELACIÓN
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenCanceledMidBackup_ShouldResumeAutosaveReleaseLockAndPropagate()
        {
            using var context = new BackupManagerTestContext();

            // Esta compuerta nunca se abre: el respaldo solo termina por cancelación.
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            using var cts = new CancellationTokenSource();

            Task<BackupReport> task = context.Manager.RunBackupAsync(ResticBackupKind.Manual, cts.Token);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);
            await cts.CancelAsync();

            await TestAssert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TestTimeout));

            // El guardado se reactivó a pesar de la cancelación.
            CollectionAssert.AreEqual(
                new[] { "repository-state", "prepare", "backup", "resume" },
                context.Log.Entries.ToArray());

            Assert.IsFalse(context.Server.IsAutosaveDisabled);

            // El candado quedó libre: un nuevo respaldo funciona.
            context.Repository.BackupGate = null;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        // ============================================================
        // VALIDACIÓN
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WithUndefinedKind_ShouldThrowBeforeCallingAnything()
        {
            using var context = new BackupManagerTestContext();

            await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => context.Manager.RunBackupAsync((ResticBackupKind)99));

            Assert.IsEmpty(context.Log.Entries);
        }
    }
}
