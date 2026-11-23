using System.Text;
using System.Text.Json;
using MinecraftBackupManager.Backup.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;

namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Guarda en prune-state.json cuándo fue el último prune exitoso y cuántos
    /// intentos fallidos hubo hoy, para que sobrevivan a reinicios.
    /// </summary>
    public sealed class PruneStateStore
    {
        public const int DefaultPruneRetriesPerDay = 0;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        private readonly ApplicationPaths _paths;
        private readonly LogManager _logManager;
        private readonly TimeProvider _timeProvider;
        private readonly int _retriesPerDay;
        private readonly object _lock = new();

        public PruneStateStore(
            ApplicationPaths paths,
            LogManager logManager,
            TimeProvider? timeProvider = null,
            int retriesPerDay = DefaultPruneRetriesPerDay)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));

            if (retriesPerDay < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retriesPerDay), "Los reintentos extra no pueden ser negativos.");
            }

            _timeProvider = timeProvider ?? TimeProvider.System;
            _retriesPerDay = retriesPerDay;
        }

        public bool ShouldPrune()
        {
            lock (_lock)
            {
                PruneState state = Load();
                DateOnly today = GetToday();

                // Ya hubo un prune exitoso hoy.
                if (state.LastSuccessDate == today)
                {
                    return false;
                }

                // Los fallos de hoy ya agotaron el primer intento más los reintentos extra.
                if (state.FailureDate == today && state.FailureCount > _retriesPerDay)
                {
                    return false;
                }

                return true;
            }
        }

        public void RecordPruneSuccess()
        {
            lock (_lock)
            {
                Save(new PruneState
                {
                    LastSuccessDate = GetToday(),
                    FailureDate = null,
                    FailureCount = 0
                });
            }
        }

        public void RecordPruneFailure()
        {
            lock (_lock)
            {
                PruneState state = Load();
                DateOnly today = GetToday();

                if (state.FailureDate == today)
                {
                    state.FailureCount++;
                }
                else
                {
                    state.FailureDate = today;
                    state.FailureCount = 1;
                }

                Save(state);
            }
        }

        // La fecha se toma en el momento de anotar, en el día local del equipo.
        private DateOnly GetToday()
        {
            return DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
        }

        // Debe llamarse con _lock tomado.
        private PruneState Load()
        {
            string json;

            try
            {
                json = File.ReadAllText(_paths.PruneStateFile, Encoding.UTF8);
            }
            catch (FileNotFoundException)
            {
                return new PruneState();
            }
            catch (DirectoryNotFoundException)
            {
                return new PruneState();
            }

            // Cualquier otro error de lectura (archivo en uso, sin permisos...) se propaga:
            // el archivo puede ser válido y no se debe sobrescribir a ciegas.

            PruneState? state;

            try
            {
                state = JsonSerializer.Deserialize<PruneState>(json, _jsonOptions);
            }
            catch (JsonException exception)
            {
                return Discard($"no es un JSON válido ({exception.Message})");
            }

            if (state is null)
            {
                return Discard("está vacío");
            }

            if (state.SchemaVersion != PruneState.CurrentSchemaVersion)
            {
                return Discard($"tiene una versión de esquema desconocida ({state.SchemaVersion})");
            }

            if (state.FailureCount < 0)
            {
                return Discard("tiene un contador de fallos negativo");
            }

            return state;
        }

        // Archivo dañado: se trata como vacío. Se sobrescribirá en la siguiente anotación.
        private PruneState Discard(string reason)
        {
            TryLog(log => log.Warning(
                LogCategory.Backup,
                $"El archivo prune-state.json {reason}. Se ignora y se sobrescribirá en la siguiente anotación."));

            return new PruneState();
        }

        // Debe llamarse con _lock tomado.
        private void Save(PruneState state)
        {
            _paths.EnsureDirectoriesExist();

            string temporaryFile = _paths.PruneStateFile + ".tmp";

            try
            {
                using (FileStream stream = new(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, state, _jsonOptions);
                }

                File.Move(temporaryFile, _paths.PruneStateFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
        }

        // Un fallo del registro nunca debe interrumpir una operación.
        public void TryLog(Action<LogManager> write)
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
