using System.Globalization;
using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;
using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Lanza un respaldo automático cada hora, en el minuto configurado (por defecto, en punto).
    /// El tipo de cada respaldo lo decide BackupKindSelector según los snapshots existentes.
    /// </summary>
    public sealed class BackupScheduler : IAsyncDisposable
    {
        public const int DefaultMinuteOfHour = 0;

        // Un ciclo que empieza con más retraso que esto (por ejemplo, tras suspender
        // el equipo) se salta: un respaldo perdido se ignora, no se recupera.
        public static readonly TimeSpan MaximumLateness = TimeSpan.FromMinutes(5);

        private readonly BackupManager _backupManager;
        private readonly IBackupRepository _repository;
        private readonly BackupKindSelector _kindSelector;
        private readonly LogManager _logManager;
        private readonly TimeProvider _timeProvider;
        private readonly int _minuteOfHour;

        private readonly object _stateLock = new();

        private CancellationTokenSource? _cancellation;
        private Task? _loopTask;
        private bool _disposed;
        private int _paused;

        public BackupScheduler(
            BackupManager backupManager,
            IBackupRepository repository,
            BackupKindSelector kindSelector,
            LogManager logManager,
            TimeProvider? timeProvider = null,
            int minuteOfHour = DefaultMinuteOfHour)
        {
            _backupManager = backupManager ?? throw new ArgumentNullException(nameof(backupManager));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _kindSelector = kindSelector ?? throw new ArgumentNullException(nameof(kindSelector));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            if (minuteOfHour is < 0 or > 59)
            {
                throw new ArgumentOutOfRangeException(nameof(minuteOfHour), "El minuto debe estar entre 0 y 59.");
            }

            _timeProvider = timeProvider ?? TimeProvider.System;
            _minuteOfHour = minuteOfHour;
        }

        /// <summary>Se lanza al terminar cada respaldo automático, con su informe.</summary>
        public event Action<BackupReport>? BackupCompleted;

        public bool IsPaused => Volatile.Read(ref _paused) == 1;

        public void Pause()
        {
            if (Interlocked.Exchange(ref _paused, 1) == 0)
            {
                TryLog(log => log.Information(LogCategory.Backup, "Respaldos automáticos en pausa."));
            }
        }

        public void Resume()
        {
            if (Interlocked.Exchange(ref _paused, 0) == 1)
            {
                TryLog(log => log.Information(LogCategory.Backup, "Respaldos automáticos reanudados."));
            }
        }

        public void Start()
        {
            lock (_stateLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (_loopTask is not null)
                {
                    throw new InvalidOperationException("El planificador ya está en marcha.");
                }

                var cancellation = new CancellationTokenSource();
                CancellationToken token = cancellation.Token;

                _cancellation = cancellation;
                _loopTask = Task.Run(() => RunLoopAsync(token));
            }
        }

        public async Task StopAsync()
        {
            CancellationTokenSource? cancellation;
            Task? loopTask;

            lock (_stateLock)
            {
                cancellation = _cancellation;
                loopTask = _loopTask;

                _cancellation = null;
                _loopTask = null;
            }

            if (cancellation is null || loopTask is null)
            {
                return;
            }

            try
            {
                await cancellation.CancelAsync();
                await loopTask;
            }
            finally
            {
                cancellation.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_stateLock)
            {
                _disposed = true;
            }

            await StopAsync();

            GC.SuppressFinalize(this);
        }

        private async Task RunLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                DateTimeOffset previousSlot = DateTimeOffset.MinValue;
                bool previousCycleRanBackup = false;

                while (!cancellationToken.IsCancellationRequested)
                {
                    DateTimeOffset now = _timeProvider.GetUtcNow();

                    // Se parte del mayor entre "ahora" y el último horario atendido: si el
                    // temporizador despertara unos milisegundos antes, no se repetiría el mismo :00.
                    DateTimeOffset slot = GetNextSlot(now > previousSlot ? now : previousSlot);

                    if (previousCycleRanBackup)
                    {
                        LogSkippedSlots(previousSlot, slot);
                    }

                    previousSlot = slot;

                    try
                    {
                        await Task.Delay(slot - now, _timeProvider, cancellationToken);

                        previousCycleRanBackup = await RunCycleAsync(slot, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                }
            }
            catch (Exception exception)
            {
                // Los ciclos ya capturan sus errores: esto no debería ocurrir nunca.
                TryLog(log => log.Critical(LogCategory.Backup, $"El planificador de respaldos se detuvo por un error inesperado: {exception.Message}"));
            }
        }

        // Debe llamarse solo desde el bucle. Devuelve true si el ciclo llegó a lanzar un respaldo.
        // Nunca lanza excepciones, salvo la cancelación pedida.
        private async Task<bool> RunCycleAsync(DateTimeOffset slot, CancellationToken cancellationToken)
        {
            string slotText = FormatLocalTime(slot);

            try
            {
                if (IsPaused)
                {
                    TryLog(log => log.Information(LogCategory.Backup, $"Ciclo de las {slotText} omitido: los respaldos automáticos están en pausa."));

                    return false;
                }

                // Se mide entre instantes reales: un cambio de horario no cuenta como retraso.
                TimeSpan lateness = _timeProvider.GetUtcNow() - slot;

                if (lateness > MaximumLateness)
                {
                    int minutes = (int)lateness.TotalMinutes;

                    TryLog(log => log.Warning(LogCategory.Backup, $"Ciclo de las {slotText} omitido: empezó con {minutes} minutos de retraso (el equipo pudo estar suspendido)."));

                    return false;
                }

                IReadOnlyList<ResticSnapshot> snapshots;

                try
                {
                    snapshots = await _repository.ListSnapshotsAsync(kind: null, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    TryLog(log => log.Error(LogCategory.Backup, $"Ciclo de las {slotText} omitido: no se pudo leer la lista de snapshots: {exception.Message}"));

                    return false;
                }

                ResticBackupKind kind = _kindSelector.Select(snapshots);

                BackupReport report = await _backupManager.RunBackupAsync(kind, cancellationToken);

                TryLog(log => log.Information(LogCategory.Backup, $"Respaldo automático ({kind.ToTag()}) de las {slotText}: {report.Status}."));

                RaiseBackupCompleted(report);

                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Error inesperado en el ciclo de las {slotText}: {exception.Message}"));

                return false;
            }
        }

        // El próximo instante estrictamente posterior a "reference" cuyo minuto local es el configurado
        // y cuyo segundo es 0. Solo se mira el minuto y el segundo locales, no la hora del reloj: así un
        // cambio de horario de verano no desplaza el cálculo. Devuelve un instante real (UTC).
        private DateTimeOffset GetNextSlot(DateTimeOffset reference)
        {
            DateTimeOffset local = TimeZoneInfo.ConvertTime(reference, _timeProvider.LocalTimeZone);

            long ticksIntoHour = local.Ticks % TimeSpan.TicksPerHour;
            long delta = _minuteOfHour * TimeSpan.TicksPerMinute - ticksIntoHour;

            if (delta <= 0)
            {
                delta += TimeSpan.TicksPerHour;
            }

            return reference.AddTicks(delta);
        }

        private string FormatLocalTime(DateTimeOffset instant)
        {
            return TimeZoneInfo.ConvertTime(instant, _timeProvider.LocalTimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        // Si el respaldo anterior duró más de una hora, los :00 que pasaron mientras tanto se omitieron.
        private void LogSkippedSlots(DateTimeOffset previousSlot, DateTimeOffset nextSlot)
        {
            int skipped = (int)Math.Round((nextSlot - previousSlot).TotalHours) - 1;

            if (skipped <= 0)
            {
                return;
            }

            string previousText = FormatLocalTime(previousSlot);

            TryLog(log => log.Warning(LogCategory.Backup, $"El respaldo de las {previousText} duró más de una hora: se omitieron {skipped} ciclo(s) automático(s) mientras estaba en curso."));
        }

        // Cada suscriptor se avisa por separado: si uno falla, los demás reciben el informe igualmente.
        private void RaiseBackupCompleted(BackupReport report)
        {
            Action<BackupReport>? handlers = BackupCompleted;

            if (handlers is null)
            {
                return;
            }

            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<BackupReport>)handler)(report);
                }
                catch (Exception exception)
                {
                    TryLog(log => log.Warning(LogCategory.Backup, $"Un suscriptor del evento BackupCompleted falló: {exception.Message}"));
                }
            }
        }

        // Un fallo del registro nunca debe interrumpir un ciclo.
        private void TryLog(Action<LogManager> write)
        {
            try
            {
                write(_logManager);
            }
            catch
            {
                // Se ignora deliberadamente.
            }
        }
    }
}
