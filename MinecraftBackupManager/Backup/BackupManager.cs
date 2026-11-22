using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Restic.Models;
using System.Net.Sockets;

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
        private readonly IWorldLockProbe _worldLockProbe;
        private readonly MinecraftServerConfig _serverConfig;
        private readonly RetentionConfig _retention;
        private readonly LogManager _logManager;
        private readonly SemaphoreSlim _backupLock = new(1, 1);

        public BackupManager(
            IBackupServer server,
            IBackupRepository repository,
            IPruneStateStore pruneState,
            IWorldLockProbe worldLockProbe,
            MinecraftServerConfig serverConfig,
            RetentionConfig retention,
            LogManager logManager)
        {
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _pruneState = pruneState ?? throw new ArgumentNullException(nameof(pruneState));
            _worldLockProbe = worldLockProbe ?? throw new ArgumentNullException(nameof(worldLockProbe));
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
            ResticRepositoryState repositoryState;

            try
            {
                repositoryState = await _repository.GetRepositoryStateAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // El servidor aún no se ha tocado: no hay nada que reactivar.
                return BuildFailedReport(kind, exception, autosaveRestored: true);
            }

            if (repositoryState != ResticRepositoryState.Ready)
            {
                string problem = DescribeRepositoryProblem(repositoryState);

                TryLog(log => log.Error(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) no iniciado: {problem}"));

                return new BackupReport(BackupStatus.Failed, problem, null, kind, AutosaveRestored: true);
            }

            // 2. Se intenta conectar por RCON antes de tocar nada en el servidor.
            try
            {
                await _server.ConnectAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsConnectionRefused(exception))
            {
                // Nadie escucha en el puerto RCON: el servidor está apagado, o RCON no está activo.
                return await RunWithoutRconAsync(kind, cancellationToken);
            }
            catch (Exception exception)
            {
                // Contraseña incorrecta, tiempo agotado, sin credenciales...: no se adivina nada.
                return BuildFailedReport(kind, exception, autosaveRestored: true);
            }

            // 3. Preparar el servidor y crear el snapshot.
            ResticBackupResult backup;

            try
            {
                await _server.PrepareForBackupAsync(cancellationToken);

                backup = await _repository.BackupAsync(_serverConfig.ServerDirectory, kind, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Cancelar no es un fallo: se reactiva el guardado y se propaga la cancelación.
                await ResumeAutosaveSafelyAsync();

                throw;
            }
            catch (Exception exception)
            {
                // Se reactiva el guardado y el fallo se convierte en un informe.
                bool restored = await ResumeAutosaveSafelyAsync();

                return BuildFailedReport(kind, exception, restored);
            }

            // Si todo fue bien, también se reactiva el guardado.
            bool autosaveRestored = await ResumeAutosaveSafelyAsync();

            // 4. Retención: solo se aplica a los respaldos automáticos.
            IReadOnlyList<string> retentionWarnings = await ApplyRetentionAsync(kind, autosaveRestored, cancellationToken);

            return BuildReport(kind, backup, autosaveRestored, retentionWarnings, serverWasOff: false);
        }

        // Se llega aquí cuando nadie escucha en el puerto RCON.
        // Debe llamarse con _backupLock tomado.
        private async Task<BackupReport> RunWithoutRconAsync(ResticBackupKind kind, CancellationToken cancellationToken)
        {
            TryLog(log => log.Information(LogCategory.Backup, "Nadie escucha en el puerto RCON. Se comprueba si el servidor está apagado."));

            SessionLockState lockState;

            try
            {
                lockState = _worldLockProbe.CheckWorldLock();
            }
            catch (Exception exception)
            {
                // Falta server.properties, falta la carpeta del mundo, level-name fuera de la carpeta...
                return BuildFailedReport(kind, exception, autosaveRestored: true);
            }

            if (lockState == SessionLockState.InUse)
            {
                return BuildServerRunningReport(kind);
            }

            // El servidor está apagado.
            if (!_serverConfig.AllowBackupWhenServerIsOff && IsAutomatic(kind))
            {
                return BuildSkippedReport(kind);
            }

            TryLog(log => log.Information(LogCategory.Backup, "El servidor está apagado: se respalda sin RCON."));

            ResticBackupResult backup;

            try
            {
                backup = await _repository.BackupAsync(_serverConfig.ServerDirectory, kind, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Con el servidor apagado no se tocó el guardado: no hay nada que reactivar.
                return BuildFailedReport(kind, exception, autosaveRestored: true);
            }

            var extraWarnings = new List<string>();

            // Si el servidor se encendió mientras se respaldaba, la copia podría ser inconsistente.
            if (IsWorldLockedNow())
            {
                extraWarnings.Add("el servidor se encendió durante el respaldo, por lo que la copia podría ser inconsistente");
            }

            extraWarnings.AddRange(await ApplyRetentionAsync(kind, autosaveRestored: true, cancellationToken));

            return BuildReport(kind, backup, autosaveRestored: true, extraWarnings, serverWasOff: true);
        }

        // Vuelve a mirar si el servidor tiene el mundo bloqueado. Si no se puede
        // comprobar, solo se anota en el log: el respaldo ya se hizo.
        private bool IsWorldLockedNow()
        {
            try
            {
                return _worldLockProbe.CheckWorldLock() == SessionLockState.InUse;
            }
            catch (Exception exception)
            {
                TryLog(log => log.Warning(LogCategory.Backup, $"No se pudo comprobar si el servidor se encendió durante el respaldo: {exception.Message}"));

                return false;
            }
        }

        private BackupReport BuildServerRunningReport(ResticBackupKind kind)
        {
            string message =
                $"El servidor parece estar en marcha, pero no se pudo conectar por RCON en {_serverConfig.RconHost}:{_serverConfig.RconPort}. " +
                "Comprueba que enable-rcon esté en true en server.properties y que el puerto sea el correcto; " +
                "si el servidor está arrancando, espera a que termine.";

            TryLog(log => log.Error(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) no iniciado: {message}"));

            return new BackupReport(BackupStatus.Failed, message, null, kind, AutosaveRestored: true);
        }

        private BackupReport BuildSkippedReport(ResticBackupKind kind)
        {
            const string message = "Respaldo omitido: el servidor está apagado y la configuración no permite respaldar con el servidor apagado.";

            TryLog(log => log.Information(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) omitido: el servidor está apagado."));

            // No se tocó ni el servidor ni el repositorio.
            return new BackupReport(BackupStatus.Skipped, message, null, kind, AutosaveRestored: true);
        }

        private static bool IsAutomatic(ResticBackupKind kind)
        {
            return kind is ResticBackupKind.Hourly or ResticBackupKind.Daily or ResticBackupKind.Weekly;
        }

        // Busca también en las excepciones internas, por si la original viene envuelta.
        private static bool IsConnectionRefused(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
            {
                if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
                {
                    return true;
                }
            }

            return false;
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

            if (!autosaveRestored)
            {
                return warnings;
            }

            // ShouldPrune se consulta al final, solo si todo lo demás permite el prune.
            bool pruneDue;

            try
            {
                pruneDue = _pruneState.ShouldPrune();
            }
            catch (Exception exception)
            {
                TryLog(log => log.Warning(LogCategory.Backup, $"No se pudo leer el estado de la compactación: {exception.Message}"));

                warnings.Add("no se pudo leer el estado de la compactación, por lo que no se compactó el repositorio");

                return warnings;
            }

            if (!pruneDue)
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

            if (!pruneSucceeded)
            {
                warnings.Add("no se pudo compactar el repositorio");
            }

            RecordPruneResult(pruneSucceeded, warnings);

            return warnings;
        }

        // Anota el resultado del prune. Si no se puede anotar, solo se avisa:
        // el respaldo ya se hizo y no debe fallar por esto.
        private void RecordPruneResult(bool succeeded, List<string> warnings)
        {
            try
            {
                if (succeeded)
                {
                    _pruneState.RecordPruneSuccess();
                }
                else
                {
                    _pruneState.RecordPruneFailure();
                }
            }
            catch (Exception exception)
            {
                TryLog(log => log.Warning(LogCategory.Backup, $"No se pudo guardar el resultado de la compactación: {exception.Message}"));

                warnings.Add("no se pudo guardar el resultado de la compactación");
            }
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

        private BackupReport BuildFailedReport(ResticBackupKind kind, Exception exception, bool autosaveRestored)
        {
            string message = $"El respaldo falló: {exception.Message}";

            if (!autosaveRestored)
            {
                message += " No se pudo reactivar el guardado automático; ejecuta 'save-on' manualmente en el servidor.";
            }

            // El detalle técnico (incluido el tipo de excepción) queda solo en el log.
            TryLog(log => log.Error(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) fallido ({exception.GetType().Name}): {exception.Message}"));

            return new BackupReport(BackupStatus.Failed, message, null, kind, autosaveRestored);
        }

        private BackupReport BuildReport(
            ResticBackupKind kind,
            ResticBackupResult backup,
            bool autosaveRestored,
            IReadOnlyList<string> extraWarnings,
            bool serverWasOff)
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

            warnings.AddRange(extraWarnings);

            if (warnings.Count == 0)
            {
                TryLog(log => log.Information(LogCategory.Backup, $"Respaldo ({kind.ToTag()}) finalizado correctamente."));

                string successMessage = serverWasOff
                    ? "Respaldo completado con el servidor apagado."
                    : "Respaldo completado.";

                return new BackupReport(BackupStatus.Completed, successMessage, backup, kind, autosaveRestored);
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
