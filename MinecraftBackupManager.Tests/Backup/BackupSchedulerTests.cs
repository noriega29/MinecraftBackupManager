using System.Globalization;
using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Backup
{
    [TestClass]
    public sealed class BackupSchedulerTests
    {
        private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

        // Del reloj inicial (14:20) al primer :00 (15:00).
        private static readonly TimeSpan UntilFirstSlot = TimeSpan.FromMinutes(40);

        // Con el repositorio vacío el selector pide weekly, y BackupManager hace esto en orden.
        private static readonly string[] FullWeeklyCycle =
        {
            "list-snapshots",
            "repository-state", "connect", "prepare", "backup", "resume",
            "forget:Weekly:4", "prune-state:should-prune"
        };

        // ============================================================
        // HORARIO
        // ============================================================

        [TestMethod]
        public async Task Scheduler_ShouldNotBackUpBeforeTheNextSlot()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();

            // 14:59.
            await context.AdvanceAsync(TimeSpan.FromMinutes(39));

            Assert.IsEmpty(context.Log.Entries);
            Assert.IsEmpty(context.Reports);
        }

        [TestMethod]
        public async Task Scheduler_ShouldBackUpAtTheNextOclock()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            CollectionAssert.AreEqual(FullWeeklyCycle, context.Log.Entries.ToArray());

            Assert.HasCount(1, context.Reports);
            Assert.AreEqual(BackupStatus.Completed, context.Reports[0].Status);
            Assert.AreEqual(ResticBackupKind.Weekly, context.Reports[0].Kind);
        }

        [TestMethod]
        public async Task Scheduler_ShouldRunOneCycleEveryHour()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();

            // 15:00.
            await context.AdvanceAsync(UntilFirstSlot);
            Assert.HasCount(1, context.Reports);

            // 15:59: todavía no.
            await context.AdvanceAsync(TimeSpan.FromMinutes(59));
            Assert.HasCount(1, context.Reports);

            // 16:00.
            await context.AdvanceAsync(TimeSpan.FromMinutes(1));
            Assert.HasCount(2, context.Reports);

            // 17:00.
            await context.AdvanceAsync(TimeSpan.FromMinutes(60));
            Assert.HasCount(3, context.Reports);
        }

        [TestMethod]
        public async Task Scheduler_WithMinute30_ShouldBackUpAtHalfPast()
        {
            await using var context = new BackupSchedulerTestContext(minuteOfHour: 30);

            await context.StartAndWaitUntilArmedAsync();

            // 14:29.
            await context.AdvanceAsync(TimeSpan.FromMinutes(9));
            Assert.IsEmpty(context.Reports);

            // 14:30.
            await context.AdvanceAsync(TimeSpan.FromMinutes(1));
            Assert.HasCount(1, context.Reports);
        }

        [TestMethod]
        public async Task Scheduler_WhenStartedExactlyOnTheMark_ShouldWaitAnHour()
        {
            await using var context = new BackupSchedulerTestContext(utcNow: "2026-11-25T15:00:00Z");

            await context.StartAndWaitUntilArmedAsync();

            // 15:59.
            await context.AdvanceAsync(TimeSpan.FromMinutes(59));
            Assert.IsEmpty(context.Reports);

            // 16:00.
            await context.AdvanceAsync(TimeSpan.FromMinutes(1));
            Assert.HasCount(1, context.Reports);
        }

        // ============================================================
        // TIPO DE RESPALDO E INFORMES
        // ============================================================

        [TestMethod]
        [DataRow(false, false, ResticBackupKind.Weekly)]
        [DataRow(false, true, ResticBackupKind.Weekly)]
        [DataRow(true, false, ResticBackupKind.Daily)]
        [DataRow(true, true, ResticBackupKind.Hourly)]
        public async Task Cycle_ShouldRequestTheKindChosenFromTheSnapshots(bool hasWeekly, bool hasDaily, ResticBackupKind expected)
        {
            await using var context = new BackupSchedulerTestContext();

            var snapshots = new List<ResticSnapshot>();

            if (hasWeekly)
            {
                snapshots.Add(BackupSchedulerTestContext.Snapshot("2026-11-24T12:00:00Z", "weekly"));
            }

            if (hasDaily)
            {
                snapshots.Add(BackupSchedulerTestContext.Snapshot("2026-11-25T03:00:00Z", "daily"));
            }

            context.Repository.Snapshots = snapshots;

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            Assert.AreEqual(expected, context.Repository.LastBackupKind);
            Assert.HasCount(1, context.Reports);
            Assert.AreEqual(expected, context.Reports[0].Kind);
        }

        [TestMethod]
        public async Task Cycle_ShouldDeliverTheReportEvenWhenTheBackupFails()
        {
            await using var context = new BackupSchedulerTestContext();

            context.Repository.State = ResticRepositoryState.NotInitialized;

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            Assert.HasCount(1, context.Reports);
            Assert.AreEqual(BackupStatus.Failed, context.Reports[0].Status);
        }

        [TestMethod]
        public async Task Cycle_WhenAnotherBackupIsRunning_ShouldDeliverAlreadyRunningReport()
        {
            await using var context = new BackupSchedulerTestContext();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            await context.StartAndWaitUntilArmedAsync();

            // Un respaldo manual se queda "trabajando" hasta que se abra la compuerta.
            Task<BackupReport> manual = context.Manager.RunBackupAsync(ResticBackupKind.Manual);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);

            await context.AdvanceAsync(UntilFirstSlot);

            Assert.HasCount(1, context.Reports);
            Assert.AreEqual(BackupStatus.AlreadyRunning, context.Reports[0].Status);

            gate.SetResult();

            BackupReport manualReport = await manual.WaitAsync(TestTimeout);

            Assert.AreEqual(BackupStatus.Completed, manualReport.Status);
        }

        // ============================================================
        // PAUSA
        // ============================================================

        [TestMethod]
        public async Task Pause_ShouldSkipTheCycleWithoutReportAndLogIt()
        {
            await using var context = new BackupSchedulerTestContext();

            // Se puede pausar antes de arrancar.
            context.Scheduler.Pause();

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            Assert.IsTrue(context.Scheduler.IsPaused);
            Assert.IsEmpty(context.Log.Entries);
            Assert.IsEmpty(context.Reports);
            StringAssert.Contains(context.ReadBackupLog(), "Ciclo de las 15:00 omitido");
        }

        [TestMethod]
        public async Task Resume_ShouldBackUpAtTheNextSlot()
        {
            await using var context = new BackupSchedulerTestContext();

            context.Scheduler.Pause();

            await context.StartAndWaitUntilArmedAsync();

            // 15:00 en pausa: se salta.
            await context.AdvanceAsync(UntilFirstSlot);
            Assert.IsEmpty(context.Reports);

            context.Scheduler.Resume();

            Assert.IsFalse(context.Scheduler.IsPaused);

            // 16:00 sin pausa: respalda.
            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.HasCount(1, context.Reports);
            CollectionAssert.AreEqual(FullWeeklyCycle, context.Log.Entries.ToArray());
        }

        [TestMethod]
        public async Task PauseAndResume_ShouldBeIdempotentAndLogOnlyRealChanges()
        {
            await using var context = new BackupSchedulerTestContext();

            context.Scheduler.Pause();
            context.Scheduler.Pause();

            Assert.IsTrue(context.Scheduler.IsPaused);

            context.Scheduler.Resume();
            context.Scheduler.Resume();

            Assert.IsFalse(context.Scheduler.IsPaused);

            string log = context.ReadBackupLog();

            Assert.AreEqual(1, CountOccurrences(log, "Respaldos automáticos en pausa."));
            Assert.AreEqual(1, CountOccurrences(log, "Respaldos automáticos reanudados."));
        }

        [TestMethod]
        public async Task Pause_ShouldNotCancelABackupInProgress()
        {
            await using var context = new BackupSchedulerTestContext();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            await context.StartAndWaitUntilArmedAsync();

            // El ciclo de las 15:00 empieza y se queda esperando en el respaldo.
            context.Clock.Advance(UntilFirstSlot);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);

            context.Scheduler.Pause();

            gate.SetResult();

            BackupReport report = await context.WaitForReportAsync();

            Assert.AreEqual(BackupStatus.Completed, report.Status);
            Assert.IsTrue(context.Scheduler.IsPaused);
        }

        // ============================================================
        // RETRASO (EL EQUIPO SE SUSPENDIÓ)
        // ============================================================

        [TestMethod]
        [DataRow(4, true)]
        [DataRow(5, true)]
        [DataRow(6, false)]
        public async Task Cycle_ShouldBeSkippedOnlyWhenItStartsMoreThanFiveMinutesLate(int minutesLate, bool shouldBackUp)
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();

            // El equipo "se suspende" y despierta con retraso: el reloj salta sin disparar nada.
            context.Clock.SetUtcNow(
                DateTimeOffset.Parse("2026-11-25T15:00:00Z", CultureInfo.InvariantCulture).AddMinutes(minutesLate));

            // Ahora sí vence el temporizador de las 15:00, pero el reloj ya marca más tarde.
            await context.AdvanceAsync(TimeSpan.Zero);

            Assert.HasCount(shouldBackUp ? 1 : 0, context.Reports);

            if (!shouldBackUp)
            {
                Assert.IsEmpty(context.Log.Entries);
                StringAssert.Contains(context.ReadBackupLog(), "retraso");
            }
        }

        [TestMethod]
        public async Task Cycle_AfterALateSkip_ShouldBackUpAtTheFollowingSlot()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();

            // Despierta a las 15:20: el ciclo de las 15:00 se pierde.
            context.Clock.SetUtcNow(DateTimeOffset.Parse("2026-11-25T15:20:00Z", CultureInfo.InvariantCulture));

            await context.AdvanceAsync(TimeSpan.Zero);

            Assert.IsEmpty(context.Reports);

            // 16:00.
            await context.AdvanceAsync(TimeSpan.FromMinutes(40));

            Assert.HasCount(1, context.Reports);
        }

        // ============================================================
        // ERRORES
        // ============================================================

        [TestMethod]
        public async Task Cycle_WhenListingSnapshotsFails_ShouldSkipLogAndKeepRunning()
        {
            await using var context = new BackupSchedulerTestContext();

            context.Repository.ListSnapshotsException = new InvalidOperationException("restic falló");

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            // No se respalda: ni se tocó el servidor ni el repositorio, salvo la lista.
            CollectionAssert.AreEqual(new[] { "list-snapshots" }, context.Log.Entries.ToArray());
            Assert.IsEmpty(context.Reports);

            string log = context.ReadBackupLog();

            StringAssert.Contains(log, "no se pudo leer la lista de snapshots");
            StringAssert.Contains(log, "restic falló");

            // El planificador sigue vivo: el siguiente ciclo funciona.
            context.Repository.ListSnapshotsException = null;

            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.HasCount(1, context.Reports);
        }

        [TestMethod]
        public async Task Cycle_WhenSomethingUnexpectedFails_ShouldLogItAndKeepRunning()
        {
            await using var context = new BackupSchedulerTestContext();

            // Una lista nula hace fallar al selector: un error que nadie previó.
            context.Repository.Snapshots = null!;

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            Assert.IsEmpty(context.Reports);
            StringAssert.Contains(context.ReadBackupLog(), "Error inesperado en el ciclo de las 15:00");

            context.Repository.Snapshots = Array.Empty<ResticSnapshot>();

            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.HasCount(1, context.Reports);
        }

        [TestMethod]
        public async Task Cycle_WhenASubscriberFails_ShouldStillNotifyTheOthersAndKeepRunning()
        {
            await using var context = new BackupSchedulerTestContext();

            int received = 0;

            context.Scheduler.BackupCompleted += _ => throw new InvalidOperationException("fallo del suscriptor");
            context.Scheduler.BackupCompleted += _ => Interlocked.Increment(ref received);

            await context.StartAndWaitUntilArmedAsync();
            await context.AdvanceAsync(UntilFirstSlot);

            Assert.AreEqual(1, received);
            Assert.HasCount(1, context.Reports);
            StringAssert.Contains(context.ReadBackupLog(), "suscriptor");

            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.AreEqual(2, received);
        }

        // ============================================================
        // ARRANCAR Y DETENER
        // ============================================================

        [TestMethod]
        public async Task Start_CalledTwice_ShouldThrowInvalidOperationException()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();

            TestAssert.Throws<InvalidOperationException>(() => context.Scheduler.Start());
        }

        [TestMethod]
        public async Task StopAsync_WithoutStart_ShouldDoNothing()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.Scheduler.StopAsync().WaitAsync(TestTimeout);

            Assert.IsEmpty(context.Log.Entries);
        }

        [TestMethod]
        public async Task StopAsync_ShouldStopSchedulingCycles()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();
            await context.Scheduler.StopAsync().WaitAsync(TestTimeout);

            context.Clock.Advance(TimeSpan.FromHours(3));

            // Se da un instante por si algún ciclo rezagado quisiera arrancar.
            await Task.Delay(100);

            Assert.IsEmpty(context.Reports);
            Assert.IsEmpty(context.Log.Entries);
        }

        [TestMethod]
        public async Task StopAsync_DuringABackup_ShouldCancelItAndResumeAutosave()
        {
            await using var context = new BackupSchedulerTestContext();

            // Esta compuerta nunca se abre: el respaldo solo termina por cancelación.
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            await context.StartAndWaitUntilArmedAsync();

            context.Clock.Advance(UntilFirstSlot);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);
            await context.Scheduler.StopAsync().WaitAsync(TestTimeout);

            // El guardado se reactivó a pesar de la cancelación y no se entregó ningún informe.
            CollectionAssert.AreEqual(
                new[] { "list-snapshots", "repository-state", "connect", "prepare", "backup", "resume" },
                context.Log.Entries.ToArray());

            Assert.IsFalse(context.Server.IsAutosaveDisabled);
            Assert.IsEmpty(context.Reports);
        }

        [TestMethod]
        public async Task DisposeAsync_ShouldStopTheSchedulerAndRejectANewStart()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();
            await context.Scheduler.DisposeAsync();

            TestAssert.Throws<ObjectDisposedException>(() => context.Scheduler.Start());
        }

        // ============================================================
        // HORARIO DE VERANO
        // ============================================================

        [TestMethod]
        public async Task Scheduler_WhenTheClockSpringsForward_ShouldStillBackUpAtTheNextRealHour()
        {
            // 06:20 UTC son las 01:20 locales. A las 02:00 locales (07:00 UTC) el reloj salta a las 03:00.
            await using var context = new BackupSchedulerTestContext(
                utcNow: "2026-11-29T06:20:00Z",
                localTimeZone: CreateDaylightSavingZone());

            await context.StartAndWaitUntilArmedAsync();

            // 40 minutos reales después son las 03:00 locales: no debe verse como un ciclo tardío.
            await context.AdvanceAsync(TimeSpan.FromMinutes(40));

            Assert.HasCount(1, context.Reports);
            Assert.DoesNotContain("retraso", context.ReadBackupLog());

            // La hora siguiente (04:00 locales) también funciona.
            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.HasCount(2, context.Reports);
        }

        [TestMethod]
        public async Task Scheduler_WhenTheClockFallsBack_ShouldBackUpOncePerRealHour()
        {
            // 05:10 UTC son las 01:10 locales (horario de verano). A las 02:00 de verano (06:00 UTC)
            // el reloj vuelve a la 01:00.
            await using var context = new BackupSchedulerTestContext(
                utcNow: "2026-12-06T05:10:00Z",
                localTimeZone: CreateDaylightSavingZone());

            await context.StartAndWaitUntilArmedAsync();

            // 06:00 UTC: son otra vez las 01:00 locales.
            await context.AdvanceAsync(TimeSpan.FromMinutes(50));

            Assert.HasCount(1, context.Reports);

            // 07:00 UTC: son las 02:00 locales. Con el cálculo anterior, este ciclo se perdía.
            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.HasCount(2, context.Reports);
        }

        // ============================================================
        // CICLOS OMITIDOS POR UN RESPALDO LARGO
        // ============================================================

        [TestMethod]
        public async Task Cycle_WhenABackupRunsPastTheNextSlots_ShouldLogTheSkippedOnes()
        {
            await using var context = new BackupSchedulerTestContext();

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            context.Repository.BackupGate = gate.Task;

            await context.StartAndWaitUntilArmedAsync();

            // El ciclo de las 15:00 empieza y se queda esperando en el respaldo.
            context.Clock.Advance(UntilFirstSlot);

            await context.Repository.BackupStarted.Task.WaitAsync(TestTimeout);

            // El respaldo "dura" dos horas y media: el reloj salta a las 17:30 sin ningún
            // temporizador que disparar (los :00 de las 16:00 y las 17:00 pasan de largo).
            context.Clock.Advance(TimeSpan.FromMinutes(150));

            gate.SetResult();

            await context.WaitForReportAsync();

            // Espera a que el planificador programe su próximo ciclo (las 18:00).
            await context.AdvanceAsync(TimeSpan.Zero);

            string log = context.ReadBackupLog();

            StringAssert.Contains(log, "El respaldo de las 15:00 duró más de una hora");
            StringAssert.Contains(log, "se omitieron 2 ciclo(s)");
        }

        [TestMethod]
        public async Task Cycle_WhenBackupsFinishWithinTheHour_ShouldNotLogSkippedSlots()
        {
            await using var context = new BackupSchedulerTestContext();

            await context.StartAndWaitUntilArmedAsync();

            // 15:00 y 16:00.
            await context.AdvanceAsync(UntilFirstSlot);
            await context.AdvanceAsync(TimeSpan.FromMinutes(60));

            Assert.HasCount(2, context.Reports);
            Assert.DoesNotContain("se omitieron", context.ReadBackupLog());
        }

        // ============================================================
        // VALIDACIÓN DEL CONSTRUCTOR
        // ============================================================

        [TestMethod]
        public async Task Constructor_WithNullArguments_ShouldThrowArgumentNullException()
        {
            await using var context = new BackupSchedulerTestContext();

            var selector = new BackupKindSelector();

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupScheduler(null!, context.Repository, selector, context.LogManager));

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupScheduler(context.Manager, null!, selector, context.LogManager));

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupScheduler(context.Manager, context.Repository, null!, context.LogManager));

            TestAssert.Throws<ArgumentNullException>(
                () => new BackupScheduler(context.Manager, context.Repository, selector, null!));
        }

        [TestMethod]
        [DataRow(-1)]
        [DataRow(60)]
        public async Task Constructor_WithMinuteOutOfRange_ShouldThrowArgumentOutOfRangeException(int minute)
        {
            await using var context = new BackupSchedulerTestContext();

            TestAssert.Throws<ArgumentOutOfRangeException>(
                () => new BackupScheduler(
                    context.Manager,
                    context.Repository,
                    new BackupKindSelector(),
                    context.LogManager,
                    context.Clock,
                    minute));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(59)]
        public async Task Constructor_WithMinuteAtLimits_ShouldBeAccepted(int minute)
        {
            await using var context = new BackupSchedulerTestContext();

            var scheduler = new BackupScheduler(
                context.Manager,
                context.Repository,
                new BackupKindSelector(),
                context.LogManager,
                context.Clock,
                minute);

            Assert.IsFalse(scheduler.IsPaused);
        }

        // ============================================================
        // AYUDANTES
        // ============================================================

        // Zona de prueba UTC-5 con un horario de verano de una semana en 2026: el 29 de noviembre a
        // las 02:00 el reloj salta a las 03:00, y el 6 de diciembre a las 02:00 vuelve a la 01:00.
        private static TimeZoneInfo CreateDaylightSavingZone()
        {
            TimeZoneInfo.TransitionTime start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), month: 11, day: 29);

            TimeZoneInfo.TransitionTime end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
                new DateTime(1, 1, 1, 2, 0, 0), month: 12, day: 6);

            TimeZoneInfo.AdjustmentRule rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                new DateTime(2026, 1, 1),
                new DateTime(2026, 12, 31),
                TimeSpan.FromHours(1),
                start,
                end);

            return TimeZoneInfo.CreateCustomTimeZone(
                "Test DST zone",
                TimeSpan.FromHours(-5),
                "Test DST zone",
                "Test standard time",
                "Test daylight time",
                new[] { rule });
        }

        private static int CountOccurrences(string text, string value)
        {
            int count = 0;
            int index = 0;

            while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }

            return count;
        }
    }
}
