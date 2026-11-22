namespace MinecraftBackupManager.Minecraft
{
    /// <summary>
    /// Calcula la ruta del archivo session.lock del mundo de un servidor Minecraft.
    /// </summary>
    public sealed class SessionLockLocator
    {
        private const string SessionLockFileName = "session.lock";

        private readonly ServerPropertiesReader _propertiesReader;

        public SessionLockLocator() : this(new ServerPropertiesReader())
        {
        }

        public SessionLockLocator(ServerPropertiesReader propertiesReader)
        {
            _propertiesReader = propertiesReader ?? throw new ArgumentNullException(nameof(propertiesReader));
        }

        /// <summary>
        /// Devuelve la ruta de session.lock: carpeta del servidor, carpeta del
        /// mundo (level-name) y el archivo. No comprueba que session.lock exista.
        /// </summary>
        public string Resolve(string serverDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverDirectory);

            if (!Path.IsPathFullyQualified(serverDirectory))
            {
                throw new ArgumentException("La ruta del servidor debe ser una ruta completa.", nameof(serverDirectory));
            }

            string levelName = _propertiesReader.ReadLevelName(serverDirectory);

            string serverPath = Path.GetFullPath(serverDirectory);
            string worldPath = Path.GetFullPath(Path.Combine(serverPath, levelName));

            // Primero la seguridad: un level-name como "..\otra-carpeta" no debe
            // sacarnos de la carpeta del servidor.
            if (!IsInside(serverPath, worldPath))
            {
                throw new InvalidDataException($"El valor level-name de server.properties ('{levelName}') apunta fuera de la carpeta del servidor.");
            }

            if (!Directory.Exists(worldPath))
            {
                throw new DirectoryNotFoundException($"No existe la carpeta del mundo '{levelName}' (level-name de server.properties) en la carpeta del servidor.");
            }

            return Path.Combine(worldPath, SessionLockFileName);
        }

        // Se compara con un separador final para que "Servidor" no se confunda
        // con "Servidor2": sin él, la segunda ruta empezaría igual que la primera.
        private static bool IsInside(string directory, string path)
        {
            return WithSeparator(path).StartsWith(WithSeparator(directory), StringComparison.OrdinalIgnoreCase);
        }

        private static string WithSeparator(string path)
        {
            return Path.EndsInDirectorySeparator(path) ? path : path + Path.DirectorySeparatorChar;
        }
    }
}
