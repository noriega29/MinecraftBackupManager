using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;

namespace MinecraftBackupManager.Configuration
{
    public sealed class ConfigurationManager
    {
        private readonly ApplicationPaths _paths;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };

        public ConfigurationManager(ApplicationPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public ApplicationConfig? Load()
        {
            if (!File.Exists(_paths.ConfigurationFile))
            {
                return null;
            }

            try
            {
                using FileStream stream = new(_paths.ConfigurationFile, FileMode.Open, FileAccess.Read, FileShare.Read);

                ApplicationConfig? config = JsonSerializer.Deserialize<ApplicationConfig>(stream, _jsonOptions);

                if (config is null)
                {
                    throw new InvalidOperationException("El archivo de configuración está vacío o no contiene una configuración válida.");
                }

                if (config.SchemaVersion != ApplicationConfig.CurrentSchemaVersion)
                {
                    throw new NotSupportedException($"La versión del esquema ({config.SchemaVersion}) no es compatible. Versión esperada: ({ApplicationConfig.CurrentSchemaVersion}).");
                }

                return config;
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException("El archivo de configuración contiene un JSON inválido.", exception);
            }
        }

        public void Save(ApplicationConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (config.SchemaVersion != ApplicationConfig.CurrentSchemaVersion)
            {
                throw new NotSupportedException("No se puede guardar una configuración con una versión de esquema incompatible.");
            }

            _paths.EnsureDirectoriesExist();

            string temporaryFile = _paths.ConfigurationFile + ".tmp";

            try
            {
                using (FileStream stream = new(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    JsonSerializer.Serialize(stream, config, _jsonOptions);
                }

                File.Move(temporaryFile, _paths.ConfigurationFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryFile))
                {
                    File.Delete(temporaryFile);
                }
            }
        }

        /// <summary>
        /// Copia config.json antes de reemplazarlo y devuelve la ruta de la copia.
        /// El original no se modifica.
        /// </summary>
        public string CopyDamagedFile(DateTimeOffset now)
        {
            string timestamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            string copyPath = $"{_paths.ConfigurationFile}.damaged-{timestamp}";

            // overwrite: false para no perder nunca una copia anterior.
            File.Copy(_paths.ConfigurationFile, copyPath, overwrite: false);

            return copyPath;
        }
    }
}
