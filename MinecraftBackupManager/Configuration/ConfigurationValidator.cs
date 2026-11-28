using System.Net;
using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Configuration
{
    public sealed class ConfigurationValidator
    {
        public ConfigurationValidationResult Validate(ApplicationConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            List<string> errors = new();

            ValidateMinecraftServer(config, errors);
            ValidateRestic(config, errors);
            ValidateRepositoryLocation(config, errors);
            ValidateRetention(config, errors);

            return new ConfigurationValidationResult(errors);
        }

        /// <summary>
        /// Revisa un solo dato, incluidos los conflictos en los que participa.
        /// Los problemas de otros datos no se informan.
        /// </summary>
        public ConfigurationValidationResult ValidateField(ApplicationConfig config, ConfigurationField field)
        {
            ArgumentNullException.ThrowIfNull(config);

            List<string> errors = new();

            switch (field)
            {
                case ConfigurationField.ServerDirectory:
                    ValidateServerDirectory(config.MinecraftServer?.ServerDirectory, errors);
                    ValidateRepositoryLocation(config, errors);
                    break;

                case ConfigurationField.RconHost:
                    ValidateRconHost(config.MinecraftServer?.RconHost, errors);
                    break;

                case ConfigurationField.RconPort:
                    ValidateRconPort(config.MinecraftServer?.RconPort ?? 0, errors);
                    break;

                case ConfigurationField.RepositoryPath:
                    ValidateRepositoryPath(config.Restic?.RepositoryPath, errors);
                    ValidateRepositoryLocation(config, errors);
                    break;

                case ConfigurationField.HourlyCount:
                    ValidateHourlyCount(config.Retention?.HourlyCount ?? 0, errors);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(field), field, "El dato de configuración no es válido.");
            }

            return new ConfigurationValidationResult(errors);
        }

        private static void ValidateMinecraftServer(ApplicationConfig config, List<string> errors)
        {
            MinecraftServerConfig? server = config.MinecraftServer;

            if (server is null)
            {
                errors.Add("No se ha definido la configuración del servidor Minecraft.");
                return;
            }

            ValidateServerDirectory(server.ServerDirectory, errors);
            ValidateRconHost(server.RconHost, errors);
            ValidateRconPort(server.RconPort, errors);
        }

        private static void ValidateServerDirectory(string? serverDirectory, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(serverDirectory))
            {
                errors.Add("La ruta del servidor Minecraft es obligatoria.");
            }
            else if (!Path.IsPathFullyQualified(serverDirectory))
            {
                errors.Add("La ruta del servidor Minecraft debe ser una ruta completa.");
            }
            else if (!Directory.Exists(serverDirectory))
            {
                errors.Add("La ruta del servidor Minecraft no existe.");
            }
        }

        private static void ValidateRconHost(string? host, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                errors.Add("La dirección del host RCON es obligatoria.");
            }
            else if (!IsValidHost(host))
            {
                errors.Add("La dirección del host RCON no tiene un formato válido.");
            }
        }

        private static void ValidateRconPort(int port, List<string> errors)
        {
            if (port < 1 || port > 65535)
            {
                errors.Add("El puerto RCON debe estar entre 1 y 65535.");
            }
        }

        private static void ValidateRestic(ApplicationConfig config, List<string> errors)
        {
            ResticConfig? restic = config.Restic;

            if (restic is null)
            {
                errors.Add("No se ha definido la configuración de Restic.");
                return;
            }

            if (!Enum.IsDefined(restic.ExecutableSource))
            {
                errors.Add("La fuente del ejecutable Restic no es válida.");
            }
            else if (restic.ExecutableSource == ResticExecutableSource.Custom)
            {
                if (string.IsNullOrWhiteSpace(restic.CustomExecutablePath))
                {
                    errors.Add("La ruta del ejecutable Restic personalizado es obligatoria.");
                }
                else if (!Path.IsPathFullyQualified(restic.CustomExecutablePath))
                {
                    errors.Add("La ruta del ejecutable Restic personalizado debe ser una ruta completa.");
                }
                else if (!File.Exists(restic.CustomExecutablePath))
                {
                    errors.Add("El ejecutable Restic personalizado no existe.");
                }
            }

            ValidateRepositoryPath(restic.RepositoryPath, errors);
        }

        private static void ValidateRepositoryPath(string? repositoryPath, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                errors.Add("La ruta del repositorio Restic es obligatoria.");
            }
            else if (!Path.IsPathFullyQualified(repositoryPath))
            {
                errors.Add("La ruta del repositorio Restic debe ser una ruta completa.");
            }
        }

        private static void ValidateRepositoryLocation(ApplicationConfig config, List<string> errors)
        {
            string? serverDirectory = config.MinecraftServer?.ServerDirectory;
            string? repositoryPath = config.Restic?.RepositoryPath;

            // Si alguna ruta falta o no es completa, ya se informó en su propia validación.
            if (!IsFullPath(serverDirectory) || !IsFullPath(repositoryPath))
            {
                return;
            }

            string server = NormalizeDirectory(serverDirectory!);
            string repository = NormalizeDirectory(repositoryPath!);

            if (repository.StartsWith(server, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("El repositorio Restic no puede estar dentro de la carpeta del servidor Minecraft.");
            }
            else if (server.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("La carpeta del servidor Minecraft no puede estar dentro del repositorio Restic.");
            }
        }

        private static void ValidateRetention(ApplicationConfig config, List<string> errors)
        {
            RetentionConfig? retention = config.Retention;

            if (retention is null)
            {
                errors.Add("No se ha definido la configuración de retención.");
                return;
            }

            ValidateHourlyCount(retention.HourlyCount, errors);
            ValidateCount(retention.DailyCount, RetentionConfig.MaximumDailyCount, "diarias (daily)", errors);
            ValidateCount(retention.WeeklyCount, RetentionConfig.MaximumWeeklyCount, "semanales (weekly)", errors);
        }

        private static void ValidateHourlyCount(int value, List<string> errors)
        {
            ValidateCount(value, RetentionConfig.MaximumHourlyCount, "por hora (hourly)", errors);
        }

        private static void ValidateCount(int value, int maximum, string description, List<string> errors)
        {
            if (value < RetentionConfig.MinimumCount || value > maximum)
            {
                errors.Add($"El número de copias {description} a conservar debe estar entre {RetentionConfig.MinimumCount} y {maximum}.");
            }
        }

        private static bool IsValidHost(string host)
        {
            if (IPAddress.TryParse(host, out _))
            {
                return true;
            }

            return Uri.CheckHostName(host) == UriHostNameType.Dns;
        }

        private static bool IsFullPath(string? path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

        private static string NormalizeDirectory(string path)
        {
            string fullPath = Path.GetFullPath(path);

            return Path.EndsInDirectorySeparator(fullPath)
                ? fullPath
                : fullPath + Path.DirectorySeparatorChar;
        }
    }
}
