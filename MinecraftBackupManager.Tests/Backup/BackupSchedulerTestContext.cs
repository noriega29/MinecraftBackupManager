using System.Diagnostics;
using System.Globalization;
using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Tests.Backup
{
    internal sealed class BackupSchedulerTestContext : IAsyncDisposable
    {
        private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

        private readonly string _rootDirectory;
        private readonly string _backupLogPath;
        private readonly object _reportsLock = new();
        private readonly List<BackupReport> _reports = new();
        private readonly SemaphoreSlim _reportSignal = new(0);
        private int _reportsConsumed;

        public BackupCallLog Log { get; } = new();

        public FakeBackupServer Server { get; }

        public FakeBackupRepository Repository { get; }

        public FakeTimeProvider Clock { get; }

        public LogManager LogManager { get; }

        public BackupManager Manager { get; }

        public BackupScheduler Scheduler { get; }

        // Todos los informes que ha entregado el evento BackupCompleted.
        public IReadOnlyList<BackupReport> Reports
        {
            get
            {
                lock (_reportsLock)
                {
                    return _reports.ToArray();
                }
            }
        }

        // Por defecto: miércoles 25 de noviembre de 2026, 14:20 (zona UTC).
        public BackupSchedulerTestContext(
            string utcNow = "2026-11-25T14:20:00Z",
            int minuteOfHour = BackupScheduler.DefaultMinuteOfHour)
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "MinecraftBackupManagerTests",
                Guid.NewGuid().ToString("N"));

            var paths = new ApplicationPaths(Path.Combine(_rootDirectory, "Data"));

            _backupLogPath = Path.Combine(paths.LogsDirectory, "backup.log");

            Clock = new FakeTimeProvider(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture));
            LogManager = new LogManager(paths);

            Server = new FakeBackupServer(Log);
            Repository = new FakeBackupRepository(Log);

            var pruneState = new FakePruneStateStore(Log);
            var worldLock = new FakeWorldLockProbe(Log);

            var serverConfig = new MinecraftServerConfig
            {
                ServerDirectory = Path.Combine(_rootDirectory, "Test Server")
            };

            Manager = new BackupManager(
                Server,
                Repository,
                pruneState,
                worldLock,
                serverConfig,
                new RetentionConfig(),
                LogManager);

            Scheduler = new BackupScheduler(
                Manager,
                Repository,
                new BackupKindSelector(Clock),
                LogManager,
                Clock,
                minuteOfHour);

            Scheduler.BackupCompleted += OnBackupCompleted;
        }

        // Arranca el planificador y espera a que haya programado su primer ciclo.
        public async Task StartAndWaitUntilArmedAsync()
        {
            Scheduler.Start();

            await Clock.WaitForActiveTimerAsync(WaitTimeout);
        }

        // Adelanta el reloj y espera a que el planificador termine el ciclo que
        // pudo dispararse y programe el siguiente.
        public async Task AdvanceAsync(TimeSpan delta)
        {
            Clock.Advance(delta);

            await WaitUntilArmedAsync();
        }

        // Devuelve el siguiente informe que todavía no se haya consumido con este método.
        public async Task<BackupReport> WaitForReportAsync()
        {
            if (!await _reportSignal.WaitAsync(WaitTimeout))
            {
                throw new TimeoutException("El planificador no entregó ningún informe.");
            }

            lock (_reportsLock)
            {
                return _reports[_reportsConsumed++];
            }
        }

        public string ReadBackupLog()
        {
            return File.Exists(_backupLogPath) ? File.ReadAllText(_backupLogPath) : string.Empty;
        }

        public static ResticSnapshot Snapshot(string time, params string[] tags)
        {
            return new ResticSnapshot(
                Id: Guid.NewGuid().ToString("N"),
                ShortId: "abcd1234",
                Time: DateTimeOffset.Parse(time, CultureInfo.InvariantCulture),
                Host: "minecraft-backup-manager",
                Paths: new[] { @"C:\Test Server" },
                Tags: tags,
                TotalBytesProcessed: null);
        }

        public async ValueTask DisposeAsync()
        {
            await Scheduler.DisposeAsync();

            _reportSignal.Dispose();

            try
            {
                Directory.Delete(_rootDirectory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void OnBackupCompleted(BackupReport report)
        {
            lock (_reportsLock)
            {
                _reports.Add(report);
            }

            _reportSignal.Release();
        }

        // Mientras un ciclo está en marcha, el planificador no tiene ningún temporizador activo.
        private async Task WaitUntilArmedAsync()
        {
            var stopwatch = Stopwatch.StartNew();

            while (Clock.ActiveTimerCount == 0)
            {
                if (stopwatch.Elapsed > WaitTimeout)
                {
                    throw new TimeoutException("El planificador no volvió a programar su próximo ciclo.");
                }

                await Task.Delay(10);
            }
        }
    }
}
