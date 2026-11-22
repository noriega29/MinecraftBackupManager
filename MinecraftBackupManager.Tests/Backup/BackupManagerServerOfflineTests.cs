using System.Net.Sockets;
using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Backup
{
    [TestClass]
    public sealed class BackupManagerServerOfflineTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        // Lo que ocurre en un respaldo con el servidor apagado: se consulta el
        // repositorio, la conexión RCON es rechazada, se comprueba el mundo,
        // se respalda y se vuelve a comprobar el mundo.
        private static readonly string[] OfflineBackupSteps =
        {
            "repository-state", "connect", "world-lock", "backup", "world-lock"
        };

        // ============================================================
        // SERVIDOR APAGADO: SE RESPALDA SIN RCON
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenConnectionIsRefusedAndWorldIsFree_ShouldBackUpWithoutRcon()
        {
            using var context = CreateOfflineContext();

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            // Sin prepare ni resume: con el servidor apagado no hay nada que desactivar.
            CollectionAssert.AreEqual(OfflineBackupSteps, context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
            Assert.IsTrue(report.AutosaveRestored);
            Assert.AreSame(context.Repository.BackupResult, report.ResticResult);
            StringAssert.Contains(report.Message, "servidor apagado");
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenServerIsOffWithAutomaticKind_ShouldApplyRetention()
        {
            using var context = CreateOfflineContext();

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                OfflineBackupSteps.Concat(new[] { "forget:Hourly:6", "prune-state:should-prune" }).ToArray(),
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenServerIsOffAndPruneIsDue_ShouldPrune()
        {
            using var context = CreateOfflineContext();

            context.PruneState.ShouldPruneResult = true;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Daily).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                OfflineBackupSteps.Concat(new[]
                {
                    "forget:Daily:7",
                    "prune-state:should-prune",
                    "prune",
                    "prune-state:success"
                }).ToArray(),
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenConnectionRefusalIsWrappedInAnotherException_ShouldStillRecognizeIt()
        {
            using var context = new BackupManagerTestContext();

            context.Server.ConnectException = new InvalidOperationException("No se pudo conectar.", ConnectionRefused());

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(OfflineBackupSteps, context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        // ============================================================
        // LA OPCIÓN DESACTIVADA
        // ============================================================

        [TestMethod]
        [DataRow(ResticBackupKind.Hourly)]
        [DataRow(ResticBackupKind.Daily)]
        [DataRow(ResticBackupKind.Weekly)]
        public async Task RunBackupAsync_WhenServerIsOffAndOptionIsDisabled_ShouldSkipAutomaticBackups(ResticBackupKind kind)
        {
            using var context = CreateOfflineContext();

            context.ServerConfig.AllowBackupWhenServerIsOff = false;
            context.PruneState.ShouldPruneResult = true;

            BackupReport report = await context.Manager.RunBackupAsync(kind).WaitAsync(TestTimeout);

            // No se respalda, ni se aplica retención, ni se consulta el estado del prune.
            CollectionAssert.AreEqual(
                new[] { "repository-state", "connect", "world-lock" },
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Skipped, report.Status);
            Assert.AreEqual(kind, report.Kind);
            Assert.IsNull(report.ResticResult);
            Assert.IsTrue(report.AutosaveRestored);
            StringAssert.Contains(report.Message, "omitido");
        }

        [TestMethod]
        [DataRow(ResticBackupKind.Initial)]
        [DataRow(ResticBackupKind.Manual)]
        public async Task RunBackupAsync_WhenServerIsOffAndOptionIsDisabled_ShouldStillBackUpInitialAndManual(ResticBackupKind kind)
        {
            using var context = CreateOfflineContext();

            context.ServerConfig.AllowBackupWhenServerIsOff = false;

            BackupReport report = await context.Manager.RunBackupAsync(kind).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(OfflineBackupSteps, context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenServerIsOnAndOptionIsDisabled_ShouldBackUpNormally()
        {
            using var context = new BackupManagerTestContext();

            // La opción solo importa con el servidor apagado: si RCON responde, ni se mira el mundo.
            context.ServerConfig.AllowBackupWhenServerIsOff = false;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[]
                {
                    "repository-state", "connect", "prepare", "backup", "resume",
                    "forget:Hourly:6",
                    "prune-state:should-prune"
                },
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        // ============================================================
        // SERVIDOR ENCENDIDO PERO SIN RESPUESTA POR RCON
        // ============================================================

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public async Task RunBackupAsync_WhenConnectionIsRefusedAndWorldIsLocked_ShouldFailRegardlessOfOption(bool optionEnabled)
        {
            using var context = CreateOfflineContext();

            context.ServerConfig.AllowBackupWhenServerIsOff = optionEnabled;
            context.WorldLock.State = SessionLockState.InUse;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "repository-state", "connect", "world-lock" },
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Failed, report.Status);
            Assert.IsNull(report.ResticResult);
            Assert.IsTrue(report.AutosaveRestored);
            StringAssert.Contains(report.Message, "RCON");
            StringAssert.Contains(report.Message, "127.0.0.1:25575");
        }

        // ============================================================
        // ERRORES AL COMPROBAR O AL RESPALDAR
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenWorldCheckThrows_ShouldReturnFailedWithItsMessage()
        {
            using var context = CreateOfflineContext();

            context.WorldLock.CheckException = new FileNotFoundException("No se encontró server.properties en la carpeta del servidor.");

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(
                new[] { "repository-state", "connect", "world-lock" },
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Failed, report.Status);
            Assert.IsTrue(report.AutosaveRestored);
            StringAssert.Contains(report.Message, "El respaldo falló");
            StringAssert.Contains(report.Message, "No se encontró server.properties");
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenBackupThrowsWithServerOff_ShouldReturnFailedWithoutRetention()
        {
            using var context = CreateOfflineContext();

            context.Repository.BackupException = new InvalidOperationException("Restic terminó con el código 1.");
            context.PruneState.ShouldPruneResult = true;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Hourly).WaitAsync(TestTimeout);

            // Sin snapshot no hay segunda comprobación del mundo ni retención.
            CollectionAssert.AreEqual(
                new[] { "repository-state", "connect", "world-lock", "backup" },
                context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.Failed, report.Status);
            Assert.IsNull(report.ResticResult);
            Assert.IsTrue(report.AutosaveRestored);
            StringAssert.Contains(report.Message, "Restic terminó con el código 1.");
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenConnectFailsForAnotherReason_ShouldReturnFailedWithoutCheckingTheWorld()
        {
            Exception[] failures =
            {
                new UnauthorizedAccessException("El servidor rechazó la contraseña RCON."),
                new TimeoutException("Se agotó el tiempo de espera al conectar."),
                new InvalidOperationException("No hay credenciales guardadas."),
                new SocketException((int)SocketError.HostUnreachable),
                new OperationCanceledException("tiempo interno agotado")
            };

            foreach (Exception failure in failures)
            {
                using var context = new BackupManagerTestContext();

                context.Server.ConnectException = failure;

                BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

                // Solo "conexión rechazada" activa la comprobación del mundo.
                CollectionAssert.AreEqual(
                    new[] { "repository-state", "connect" },
                    context.Log.Entries.ToArray(),
                    $"Con {failure.GetType().Name} no debía comprobarse el mundo.");

                Assert.AreEqual(BackupStatus.Failed, report.Status, failure.GetType().Name);
                Assert.IsNull(report.ResticResult, failure.GetType().Name);
                Assert.IsTrue(report.AutosaveRestored, failure.GetType().Name);
                StringAssert.Contains(report.Message, "El respaldo falló");
                StringAssert.Contains(report.Message, failure.Message);
            }
        }

        // ============================================================
        // EL SERVIDOR SE ENCIENDE DURANTE EL RESPALDO
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenServerStartsDuringBackup_ShouldReturnCompletedWithWarnings()
        {
            using var context = CreateOfflineContext();

            // La primera comprobación dice "apagado" y la segunda, "encendido".
            context.WorldLock.QueuedStates.Enqueue(SessionLockState.Free);
            context.WorldLock.QueuedStates.Enqueue(SessionLockState.InUse);

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            CollectionAssert.AreEqual(OfflineBackupSteps, context.Log.Entries.ToArray());

            Assert.AreEqual(BackupStatus.CompletedWithWarnings, report.Status);
            Assert.IsNotNull(report.ResticResult);
            StringAssert.Contains(report.Message, "se encendió durante el respaldo");
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenSecondWorldCheckFails_ShouldStillCompleteBecauseBackupIsDone()
        {
            using var context = CreateOfflineContext();

            // La primera comprobación funciona; la segunda falla.
            context.WorldLock.CheckException = new IOException("disco desconectado");
            context.WorldLock.FailFromCall = 2;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Completed, report.Status);
            Assert.IsNotNull(report.ResticResult);
        }

        // ============================================================
        // CANCELACIÓN
        // ============================================================

        [TestMethod]
        public async Task RunBackupAsync_WhenCanceledWhileConnecting_ShouldPropagateInsteadOfReturningFailed()
        {
            using var context = new BackupManagerTestContext();

            using var cts = new CancellationTokenSource();

            await cts.CancelAsync();

            context.Server.ConnectException = new OperationCanceledException(cts.Token);

            await TestAssert.ThrowsAsync<OperationCanceledException>(
                () => context.Manager.RunBackupAsync(ResticBackupKind.Manual, cts.Token));

            CollectionAssert.AreEqual(
                new[] { "repository-state", "connect" },
                context.Log.Entries.ToArray());
        }

        [TestMethod]
        public async Task RunBackupAsync_WhenCanceledDuringBackupWithServerOff_ShouldPropagateAndReleaseLock()
        {
            using var context = CreateOfflineContext();

            // Esta compuerta nunca se abre: el respaldo solo termina por cancelación.
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            using var cts = new CancellationTokenSource();

            Task<BackupReport> task = context.Manager.RunBackupAsync(ResticBackupKind.Manual, cts.Token);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);
            await cts.CancelAsync();

            await TestAssert.ThrowsAsync<OperationCanceledException>(() => task.WaitAsync(TestTimeout));

            CollectionAssert.AreEqual(
                new[] { "repository-state", "connect", "world-lock", "backup" },
                context.Log.Entries.ToArray());

            // El candado quedó libre: un nuevo respaldo funciona.
            context.Repository.BackupGate = null;

            BackupReport report = await context.Manager.RunBackupAsync(ResticBackupKind.Manual).WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Completed, report.Status);
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        private static SocketException ConnectionRefused()
        {
            return new SocketException((int)SocketError.ConnectionRefused);
        }

        // Un contexto donde la conexión RCON es rechazada, como con el servidor apagado.
        // El mundo está libre por defecto.
        private static BackupManagerTestContext CreateOfflineContext()
        {
            var context = new BackupManagerTestContext();

            context.Server.ConnectException = ConnectionRefused();

            return context;
        }
    }
}
