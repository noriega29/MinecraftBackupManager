namespace MinecraftBackupManager.Infrastructure
{
    public sealed class ApplicationPaths
    {
        private const string ApplicationDirectoryName = "Minecraft Backup Manager";

        public string DataDirectory { get; }

        public string ConfigurationFile => Path.Combine(DataDirectory, "config.json");

        public string CredentialsFile => Path.Combine(DataDirectory, "credentials.dat");

        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");

        public ApplicationPaths()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (string.IsNullOrWhiteSpace(localAppData))
            {
                throw new InvalidOperationException("No se pudo determinar el directorio LocalAppData.");
            }

            DataDirectory = Path.Combine(localAppData, ApplicationDirectoryName);
        }

        // Constructor destinado a las pruebas automatizadas.
        internal ApplicationPaths(string dataDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

            if (!Path.IsPathFullyQualified(dataDirectory))
            {
                throw new ArgumentException(
                    "El directorio debe ser una ruta completa.",
                    nameof(dataDirectory));
            }

            DataDirectory = dataDirectory;
        }

        public void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
    }
}
