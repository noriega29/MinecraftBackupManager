using System.Globalization;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;
using MinecraftBackupManager.Restic.Models;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Backup;

namespace MinecraftBackupManager.Restic
{
    public sealed class ResticClient : IBackupRepository
    {
        private const int RepositoryDoesNotExistExitCode = 10;
        private const int WrongPasswordExitCode = 12;
        private const int IncompleteBackupExitCode = 3;
        private const int MaxLoggedWarnings = 20;
        private const string BackupHostName = "minecraft-backup-manager";

        private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan RepositoryCheckTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan InitializeTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ListSnapshotsTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan UnlockTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ForgetTimeout = TimeSpan.FromSeconds(60);

        private readonly ResticConfig _config;
        private readonly CredentialManager _credentialManager;
        private readonly ResticExecutableLocator _locator;
        private readonly ResticProcessRunner _runner;
        private readonly LogManager _logManager;

        public ResticClient(
            ResticConfig config,
            CredentialManager credentialManager,
            ResticExecutableLocator locator,
            ResticProcessRunner runner,
            LogManager logManager)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
            _locator = locator ?? throw new ArgumentNullException(nameof(locator));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
        }

        public async Task<string> GetVersionAsync(CancellationToken cancellationToken = default)
        {
            var result = await RunResticAsync(
                new[] { "version" },
                VersionTimeout,
                usesRepository: false,
                cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Restic terminó con el código {result.ExitCode} al consultar su versión: {result.StandardError.Trim()}");
            }

            string? versionLine = result.StandardOutput
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();

            if (string.IsNullOrEmpty(versionLine))
            {
                throw new InvalidDataException("Restic no devolvió información de versión.");
            }

            return versionLine;
        }

        public async Task<ResticRepositoryState> GetRepositoryStateAsync(CancellationToken cancellationToken = default)
        {
            var result = await RunResticAsync(
                new[] { "cat", "config" },
                RepositoryCheckTimeout,
                usesRepository: true,
                cancellationToken);

            ResticRepositoryState state = result.ExitCode switch
            {
                0 => ResticRepositoryState.Ready,
                RepositoryDoesNotExistExitCode => ResticRepositoryState.NotInitialized,
                WrongPasswordExitCode => ResticRepositoryState.WrongPassword,
                _ => throw new InvalidOperationException($"Restic terminó con el código {result.ExitCode} al comprobar el repositorio: {result.StandardError.Trim()}")
            };

            TryLog(log => log.Information(LogCategory.Application, $"Estado del repositorio Restic: {state}."));

            return state;
        }

        public async Task InitializeRepositoryAsync(CancellationToken cancellationToken = default)
        {
            var result = await RunResticAsync(
                new[] { "init" },
                InitializeTimeout,
                usesRepository: true,
                cancellationToken);

            if (result.ExitCode != 0)
            {
                TryLog(log => log.Error(LogCategory.Application, $"Falló la inicialización del repositorio Restic (código {result.ExitCode})."));

                throw new InvalidOperationException(
                    $"Restic terminó con el código {result.ExitCode} al inicializar el repositorio: {result.StandardError.Trim()}");
            }

            TryLog(log => log.Information(LogCategory.Application, "Repositorio Restic inicializado."));
        }

        public async Task<ResticBackupResult> BackupAsync(string sourcePath, ResticBackupKind kind, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

            if (!Path.IsPathFullyQualified(sourcePath))
            {
                throw new ArgumentException("La ruta a respaldar debe ser una ruta completa.", nameof(sourcePath));
            }

            if (!Directory.Exists(sourcePath))
            {
                throw new DirectoryNotFoundException($"No existe la carpeta a respaldar: {sourcePath}");
            }

            string tag = kind.ToTag();

            TryLog(log => log.Information(LogCategory.Backup, $"Iniciando respaldo ({tag}) de: {sourcePath}"));

            ProcessExecutionResult result;

            try
            {
                result = await RunResticAsync(
                    new[]
                    {
                        "backup",
                        "--json",
                        "--exclude", "session.lock",
                        "--host", BackupHostName,
                        "--tag", tag,
                        sourcePath
                    },
                    timeout: null,
                    usesRepository: true,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                TryLog(log => log.Warning(LogCategory.Backup, "Respaldo cancelado."));

                throw;
            }

            if (result.ExitCode != 0 && result.ExitCode != IncompleteBackupExitCode)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Falló el respaldo (código {result.ExitCode})."));

                throw new InvalidOperationException($"Restic terminó con el código {result.ExitCode} al hacer el respaldo: {result.StandardError.Trim()}");
            }

            ResticBackupResult backup;

            try
            {
                backup = ResticBackupOutputParser.Parse(
                    result.StandardOutput,
                    result.StandardError,
                    isIncomplete: result.ExitCode == IncompleteBackupExitCode);
            }
            catch (InvalidDataException exception)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"No se pudo interpretar el resultado del respaldo: {exception.Message}"));

                throw;
            }

            LogBackupResult(backup);

            return backup;
        }

        public async Task<IReadOnlyList<ResticSnapshot>> ListSnapshotsAsync(
            ResticBackupKind? kind = null,
            CancellationToken cancellationToken = default)
        {
            var arguments = new List<string> { "snapshots", "--json" };

            if (kind is not null)
            {
                arguments.Add("--tag");
                arguments.Add(kind.Value.ToTag());
            }

            var result = await RunResticAsync(arguments, ListSnapshotsTimeout, usesRepository: true, cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Restic terminó con el código {result.ExitCode} al listar los snapshots: {result.StandardError.Trim()}");
            }

            return ResticSnapshotListParser.Parse(result.StandardOutput);
        }

        public async Task UnlockAsync(CancellationToken cancellationToken = default)
        {
            // Sin --remove-all: solo se eliminan los bloqueos obsoletos, nunca los de una operación en marcha.
            var result = await RunResticAsync(new[] { "unlock" }, UnlockTimeout, usesRepository: true, cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Restic terminó con el código {result.ExitCode} al limpiar los bloqueos del repositorio: {result.StandardError.Trim()}");
            }

            TryLog(log => log.Information(LogCategory.Backup, "Se limpiaron los bloqueos obsoletos del repositorio."));
        }

        public async Task ForgetAsync(ResticBackupKind kind, int keepLast, CancellationToken cancellationToken = default)
        {
            if (kind is ResticBackupKind.Initial or ResticBackupKind.Manual)
            {
                throw new ArgumentException("Las copias 'initial' y 'manual' no se eliminan automáticamente.", nameof(kind));
            }

            string tag = kind.ToTag();

            if (keepLast < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(keepLast), "Debe conservarse al menos una copia.");
            }

            // forget necesita el repositorio en exclusiva: un bloqueo huérfano lo bloquearía.
            await UnlockAsync(cancellationToken);

            var result = await RunResticAsync(
                new[]
                {
                    "forget",
                    "--tag", tag,
                    "--keep-last", keepLast.ToString(CultureInfo.InvariantCulture),
                    "--host", BackupHostName
                },
                ForgetTimeout,
                usesRepository: true,
                cancellationToken);

            if (result.ExitCode != 0)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Falló la retención de las copias '{tag}' (código {result.ExitCode})."));

                throw new InvalidOperationException(
                    $"Restic terminó con el código {result.ExitCode} al aplicar la retención: {result.StandardError.Trim()}");
            }

            TryLog(log => log.Information(LogCategory.Backup, $"Retención aplicada a las copias '{tag}': se conservan las últimas {keepLast}."));
        }

        public async Task PruneAsync(CancellationToken cancellationToken = default)
        {
            await UnlockAsync(cancellationToken);

            var result = await RunResticAsync(new[] { "prune" }, timeout: null, usesRepository: true, cancellationToken);

            if (result.ExitCode != 0)
            {
                TryLog(log => log.Error(LogCategory.Backup, $"Falló la compactación del repositorio (código {result.ExitCode})."));

                throw new InvalidOperationException(
                    $"Restic terminó con el código {result.ExitCode} al compactar el repositorio: {result.StandardError.Trim()}");
            }

            TryLog(log => log.Information(LogCategory.Backup, "Repositorio compactado."));
        }

        private void LogBackupResult(ResticBackupResult backup)
        {
            string shortId = backup.SnapshotId.Length > 8 ? backup.SnapshotId[..8] : backup.SnapshotId;
            string seconds = backup.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);

            string summary =
                $"Snapshot {shortId}: {backup.FilesNew} nuevos, {backup.FilesChanged} modificados, " +
                $"{backup.FilesUnmodified} sin cambios; {backup.DataAdded} bytes añadidos; {seconds} s.";

            if (!backup.IsIncomplete)
            {
                TryLog(log => log.Information(LogCategory.Backup, $"Respaldo completado. {summary}"));

                return;
            }

            TryLog(log => log.Warning(
                LogCategory.Backup,
                $"Respaldo incompleto: no se pudieron leer {backup.Warnings.Count} elemento(s). {summary}"));

            foreach (ResticBackupWarning warning in backup.Warnings.Take(MaxLoggedWarnings))
            {
                TryLog(log => log.Warning(LogCategory.Backup, $"No se pudo leer: {warning.Item}"));
            }
        }

        private async Task<ProcessExecutionResult> RunResticAsync(
            IReadOnlyList<string> arguments,
            TimeSpan? timeout,
            bool usesRepository,
            CancellationToken cancellationToken)
        {
            string executablePath = _locator.Resolve(_config);

            Dictionary<string, string>? environment = null;

            if (usesRepository)
            {
                if (string.IsNullOrWhiteSpace(_config.RepositoryPath))
                {
                    throw new InvalidOperationException("La ruta del repositorio Restic no está configurada.");
                }

                // La contraseña solo se carga cuando hace falta y no se guarda como campo.
                environment = new Dictionary<string, string>
                {
                    ["RESTIC_REPOSITORY"] = _config.RepositoryPath,
                    ["RESTIC_PASSWORD"] = LoadRepositoryPassword()
                };
            }

            return await _runner.RunAsync(executablePath, arguments, environment, timeout, cancellationToken);
        }

        private string LoadRepositoryPassword()
        {
            var credentials = _credentialManager.Load();

            if (credentials is null)
            {
                throw new InvalidOperationException("No hay credenciales guardadas. Configura la contraseña del repositorio Restic antes de continuar.");
            }

            if (string.IsNullOrEmpty(credentials.ResticRepositoryPassword))
            {
                throw new InvalidOperationException("La contraseña del repositorio Restic no está configurada.");
            }

            return credentials.ResticRepositoryPassword;
        }

        // Un fallo del registro nunca debe interrumpir una operación.
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
