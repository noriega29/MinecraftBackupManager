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
                DateTime lastSlot = DateTime.MinValue;

                while (!cancellationToken.IsCancellationRequested)
                {
                    DateTime now = _timeProvider.GetLocalNow().DateTime;

                    // Se parte del mayor entre "ahora" y el último horario atendido: si el
                    // temporizador despertara unos milisegundos antes, no se repetiría el mismo :00.
                    DateTime slot = GetNextSlot(now > lastSlot ? now : lastSlot);

                    lastSlot = slot;

                    try
                    {
                        await Task.Delay(slot - now, _timeProvider, cancellationToken);
                        await RunCycleAsync(slot, cancellationToken);
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

        // Debe llamarse solo desde el bucle. Nunca lanza excepciones, salvo la cancelación pedida.
        private async Task RunCycleAsync(DateTime slot, CancellationToken cancellationToken)
        {
            string slotText = slot.ToString("HH:mm", CultureInfo.InvariantCulture);

            try
            {
                if (IsPaused)
                {
                    TryLog(log => log.Information(LogCategory.Backup, $"Ciclo de las {slotText} omitido: los respaldos automáticos están en pausa."));

                    return;
                }

                TimeSpan lateness = _timeProvider.GetLocalNow().DateTime - slot;

                if (lateness > MaximumLateness)
                {
                    int minutes = (int)lateness.TotalMinutes;

                    TryLog(log => log.Warning(LogCategory.Backup, $"Ciclo de las {slotText} omitido: empezó con {minutes} minutos de retraso (el equipo pudo estar suspendido)."));

                    return;
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

                    return;
                }

                ResticBackupKind kind = _kindSelector.Select(snapshots);

                BackupReport report = await _backupManager.RunBackupAsync(kind, cancellationToken);

                TryLog(log => log.Information(LogCategory.Backup, $"Respaldo automático ({kind.ToTag()}) de las {slotText}: {report.Status}."));

                RaiseBackupCompleted(report);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Error inesperado en el ciclo de las {slotText}: {exception.Message}"));
            }
        }

        // El próximo instante estrictamente posterior a "reference" con el minuto configurado y segundo 0.
        // Se trabaja con la hora local: en zonas con desfases de 30 o 45 minutos, el minuto UTC no sirve.
        private DateTime GetNextSlot(DateTime reference)
        {
            var candidate = new DateTime(reference.Year, reference.Month, reference.Day, reference.Hour, _minuteOfHour, 0);

            return candidate > reference ? candidate : candidate.AddHours(1);
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
