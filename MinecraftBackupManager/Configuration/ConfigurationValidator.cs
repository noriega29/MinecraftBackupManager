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
            ValidateRetention(config, errors);

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
            
            if (string.IsNullOrWhiteSpace(server.ServerDirectory))
            {
                errors.Add("La ruta del servidor Minecraft es obligatoria.");
            }
            else
            {
                if (!Path.IsPathFullyQualified(server.ServerDirectory))
                {
                    errors.Add("La ruta del servidor Minecraft debe ser una ruta completa.");
                }
                else if (!Directory.Exists(server.ServerDirectory))
                {
                    errors.Add("La ruta del servidor Minecraft no existe.");
                }
            }

            if (string.IsNullOrWhiteSpace(server.RconHost))
            {
                errors.Add("La dirección del host RCON es obligatoria.");
            }
            else if (!IsValidHost(server.RconHost))
            {
                errors.Add("La dirección del host RCON no tiene un formato válido.");
            }

            if (server.RconPort < 1 || server.RconPort > 65535)
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

            if (string.IsNullOrWhiteSpace(restic.RepositoryPath))
            {
                errors.Add("La ruta del repositorio Restic es obligatoria.");
            }
            else if (!Path.IsPathFullyQualified(restic.RepositoryPath))
            {
                errors.Add("La ruta del repositorio Restic debe ser una ruta completa.");
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

            ValidateCount(retention.HourlyCount, RetentionConfig.MaximumHourlyCount, "por hora (hourly)", errors);
            ValidateCount(retention.DailyCount, RetentionConfig.MaximumDailyCount, "diarias (daily)", errors);
            ValidateCount(retention.WeeklyCount, RetentionConfig.MaximumWeeklyCount, "semanales (weekly)", errors);
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
    }
}
