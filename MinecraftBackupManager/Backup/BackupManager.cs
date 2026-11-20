using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;
using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Restic.Models;

namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Coordina un respaldo completo: comprueba el repositorio, prepara el
    /// servidor, crea el snapshot y reactiva el guardado automático.
    /// </summary>
    public sealed class BackupManager
    {
        private readonly IBackupServer _server;
        private readonly IBackupRepository _repository;
        private readonly IPruneStateStore _pruneState;
        private readonly MinecraftServerConfig _serverConfig;
        private readonly RetentionConfig _retention;
        private readonly LogManager _logManager;
        private readonly SemaphoreSlim _backupLock = new(1, 1);

        public BackupManager(
            IBackupServer server,
            IBackupRepository repository,
            IPruneStateStore pruneState,
            MinecraftServerConfig serverConfig,
            RetentionConfig retention,
            LogManager logManager)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _pruneState = pruneState ?? throw new ArgumentNullException(nameof(pruneState));
            _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
            _retention = retention ?? throw new ArgumentNullException(nameof(retention));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        }

        public async Task<BackupReport> RunBackupAsync(ResticBackupKind kind, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "El tipo de respaldo no es válido.");
            }

            // Wait(0): si hay otro respaldo en curso, no se espera ni se lanza una excepción.
            if (!_backupLock.Wait(0))
            {
                TryLog(log => log.Warning(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) rechazado: ya hay otro respaldo en curso."));

                // Este respaldo no tocó el servidor, así que no dejó nada sin reactivar.
                return new BackupReport(BackupStatus.AlreadyRunning, "Ya hay un respaldo en curso.", null, kind, AutosaveRestored: true);
            }

            try
            {
                return await RunCoreAsync(kind, cancellationToken);
            }
            finally
            {
                _backupLock.Release();
            }
        }

        // Debe llamarse con _backupLock tomado.
        private async Task<BackupReport> RunCoreAsync(ResticBackupKind kind, CancellationToken cancellationToken)
        {
            // 1. Se comprueba el repositorio antes de tocar el servidor: no tiene
            //    sentido desactivar el guardado de un respaldo que no puede hacerse.
            ResticRepositoryState repositoryState = await _repository.GetRepositoryStateAsync(cancellationToken);

            if (repositoryState != ResticRepositoryState.Ready)
            {
                string problem = DescribeRepositoryProblem(repositoryState);

                TryLog(log => log.Error(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) no iniciado: {problem}"));

                return new BackupReport(BackupStatus.Failed, problem, null, kind, AutosaveRestored: true);
            }

            // 2. Preparar el servidor, crear el snapshot y reactivar el guardado.
            ResticBackupResult backup;
            bool autosaveRestored;

            try
            {
                await _server.PrepareForBackupAsync(cancellationToken);

                backup = await _repository.BackupAsync(_serverConfig.ServerDirectory, kind, cancellationToken);
            }
            finally
            {
                // Siempre se intenta reactivar el guardado, incluso si algo falló o se canceló.
                autosaveRestored = await ResumeAutosaveSafelyAsync();
            }

            // 3. Retención: solo se aplica a los respaldos automáticos.
            IReadOnlyList<string> retentionWarnings = await ApplyRetentionAsync(kind, autosaveRestored, cancellationToken);

            return BuildReport(kind, backup, autosaveRestored, retentionWarnings);
        }

        // Devuelve true si se confirmó la reactivación. Nunca lanza excepciones:
        // se usa dentro de un finally y no debe tapar el error original.
        private async Task<bool> ResumeAutosaveSafelyAsync()
        {
            try
            {
                // CancellationToken.None: una cancelación previa no debe impedir enviar "save-on".
                await _server.ResumeAutosaveAsync(CancellationToken.None);

                return true;
            }
            catch (Exception exception)
            {
                TryLog(log => log.Critical(LogCategory.Backup, $"No se pudo reactivar el guardado automático tras el respaldo: {exception.Message}"));

                return false;
            }
        }

        // Aplica forget y, si corresponde, prune. Devuelve las advertencias que
        // deben aparecer en el informe. Un fallo de la retención nunca convierte
        // en fallido un respaldo que ya creó su snapshot.
        private async Task<IReadOnlyList<string>> ApplyRetentionAsync(
            ResticBackupKind kind,
            bool autosaveRestored,
            CancellationToken cancellationToken)
        {
            var warnings = new List<string>();

            int? keepLast = GetKeepLast(kind);

            // initial y manual nunca se borran automáticamente.
            if (keepLast is null)
            {
                return warnings;
            }

            try
            {
                await _repository.ForgetAsync(kind, keepLast.Value, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Falló la retención de las copias '{kind.ToTag()}': {exception.Message}"));

                warnings.Add("no se pudieron eliminar las copias antiguas");

                // Sin un forget exitoso no se intenta el prune.
                return warnings;
            }

            // ShouldPrune se consulta al final, solo si todo lo demás permite el prune.
            if (!autosaveRestored || !_pruneState.ShouldPrune())
            {
                return warnings;
            }

            bool pruneSucceeded;

            try
            {
                await _repository.PruneAsync(cancellationToken);

                pruneSucceeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Una cancelación no es un fallo del prune: no se anota nada.
                throw;
            }
            catch (Exception exception)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Falló la compactación del repositorio: {exception.Message}"));

                pruneSucceeded = false;
            }

            // Se anota fuera del try: si anotar fallara, no debe confundirse con un fallo del prune.
            if (pruneSucceeded)
            {
                _pruneState.RecordPruneSuccess();
            }
            else
            {
                _pruneState.RecordPruneFailure();

                warnings.Add("no se pudo compactar el repositorio");
            }

            return warnings;
        }

        // Cuántas copias de ese tipo se conservan, o null si el tipo no se borra automáticamente.
        private int? GetKeepLast(ResticBackupKind kind)
        {
            return kind switch
            {
                ResticBackupKind.Hourly => _retention.HourlyCount,
                ResticBackupKind.Daily => _retention.DailyCount,
                ResticBackupKind.Weekly => _retention.WeeklyCount,
                _ => null
            };
        }

        private BackupReport BuildReport(ResticBackupKind kind, ResticBackupResult backup, bool autosaveRestored, IReadOnlyList<string> retentionWarnings)
        {
            var warnings = new List<string>();

            if (backup.IsIncomplete)
            {
                warnings.Add($"no se pudieron leer {backup.Warnings.Count} elemento(s)");
            }

            if (!autosaveRestored)
            {
                warnings.Add("no se pudo reactivar el guardado automático; ejecuta 'save-on' manualmente en el servidor");
            }

            warnings.AddRange(retentionWarnings);

            if (warnings.Count == 0)
            {
                TryLog(log => log.Information(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) finalizado correctamente."));

                return new BackupReport(BackupStatus.Completed, "Respaldo completado.", backup, kind, autosaveRestored);
            }

            string message = $"Respaldo completado con advertencias: {string.Join("; ", warnings)}.";

            TryLog(log => log.Warning(LogCategory.Backup, message));

            return new BackupReport(BackupStatus.CompletedWithWarnings, message, backup, kind, autosaveRestored);
        }

        private static string DescribeRepositoryProblem(ResticRepositoryState state)
        {
            return state switch
            {
                ResticRepositoryState.NotInitialized => "El repositorio de copias no está inicializado.",
                ResticRepositoryState.WrongPassword => "La contraseña del repositorio de copias es incorrecta.",
                _ => "El repositorio de copias no está disponible."
            };
        }

        // Un fallo del registro nunca debe interrumpir un respaldo.
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
