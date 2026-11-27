using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Security;
using MinecraftBackupManager.Security.Models;

namespace MinecraftBackupManager.Configuration
{
    /// <summary>
    /// Revisa la configuración y las credenciales guardadas y devuelve el primer
    /// problema encontrado. Solo consulta: no escribe ni ejecuta nada.
    /// </summary>
    public sealed class SetupStatusChecker
    {
        private readonly ConfigurationManager _configurationManager;
        private readonly ConfigurationValidator _validator;
        private readonly CredentialManager _credentialManager;

        public SetupStatusChecker(
            ConfigurationManager configurationManager,
            ConfigurationValidator validator,
            CredentialManager credentialManager)
        {
            _configurationManager = configurationManager ?? throw new ArgumentNullException(nameof(configurationManager));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
            _credentialManager = credentialManager ?? throw new ArgumentNullException(nameof(credentialManager));
        }

        // Los errores de lectura del disco (IOException) no se capturan:
        // el archivo puede estar bien y no se debe dar por dañado.
        public SetupStatus Check()
        {
            ApplicationConfig? config;

            try
            {
                config = _configurationManager.Load();
            }
            catch (InvalidOperationException exception)
            {
                return new SetupStatus(SetupState.ConfigurationDamaged, null, new[] { exception.Message });
            }
            catch (NotSupportedException exception)
            {
                return new SetupStatus(SetupState.ConfigurationUnsupported, null, new[] { exception.Message });
            }

            if (config is null)
            {
                return new SetupStatus(SetupState.NotConfigured, null, Array.Empty<string>());
            }

            ConfigurationValidationResult validation = _validator.Validate(config);

            if (!validation.IsValid)
            {
                return new SetupStatus(SetupState.ConfigurationInvalid, config, validation.Errors);
            }

            return CheckCredentials(config);
        }

        private SetupStatus CheckCredentials(ApplicationConfig config)
        {
            CredentialsData? credentials;

            try
            {
                credentials = _credentialManager.Load();
            }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException)
            {
                return new SetupStatus(SetupState.CredentialsUnreadable, config, new[] { exception.Message });
            }

            if (credentials is null
                || string.IsNullOrEmpty(credentials.RconPassword)
                || string.IsNullOrEmpty(credentials.ResticRepositoryPassword))
            {
                return new SetupStatus(SetupState.CredentialsMissing, config, Array.Empty<string>());
            }

            return new SetupStatus(SetupState.Ready, config, Array.Empty<string>());
        }
    }
}
