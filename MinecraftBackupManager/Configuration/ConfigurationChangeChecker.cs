using System.Text.Json;
using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Configuration
{
    /// <summary>
    /// Decide si se puede aceptar el cambio de un dato, sin modificar la configuración original.
    /// </summary>
    public sealed class ConfigurationChangeChecker
    {
        private const string ServerPropertiesFileName = "server.properties";

        private readonly ConfigurationValidator _validator;

        public ConfigurationChangeChecker(ConfigurationValidator validator)
        {
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public ConfigurationChangeResult Check(
            ApplicationConfig current,
            ConfigurationField field,
            Action<ApplicationConfig> change)
        {
            ArgumentNullException.ThrowIfNull(current);
            ArgumentNullException.ThrowIfNull(change);

            ApplicationConfig candidate = Copy(current);

            change(candidate);

            var errors = new List<string>(_validator.ValidateField(candidate, field).Errors);

            // Solo tiene sentido mirar dentro de la carpeta si la ruta ya es válida.
            if (field == ConfigurationField.ServerDirectory
                && errors.Count == 0
                && !File.Exists(Path.Combine(candidate.MinecraftServer.ServerDirectory, ServerPropertiesFileName)))
            {
                errors.Add("La carpeta no contiene server.properties: no parece la carpeta de un servidor Minecraft.");
            }

            return new ConfigurationChangeResult(candidate, errors);
        }

        // Copia completa: convertir a JSON y volver crea objetos nuevos.
        private static ApplicationConfig Copy(ApplicationConfig config)
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(config);

            ApplicationConfig copy = JsonSerializer.Deserialize<ApplicationConfig>(data)!;

            // Un config.json editado a mano puede traer una sección como null.
            copy.MinecraftServer ??= new MinecraftServerConfig();
            copy.Restic ??= new ResticConfig();
            copy.Retention ??= new RetentionConfig();

            return copy;
        }
    }
}
