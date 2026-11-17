using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Restic
{
    public sealed class ResticExecutableLocator
    {
        private readonly string _baseDirectory;

        public ResticExecutableLocator() : this (AppContext.BaseDirectory) { }

        // Constructor destinado a las pruebas automatizadas.
        internal ResticExecutableLocator(string baseDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

            _baseDirectory = baseDirectory;
        }

        public string Resolve(ResticConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            string path;

            switch (config.ExecutableSource)
            {
                case ResticExecutableSource.Bundled:
                    path = Path.Combine(_baseDirectory, "Tools", "restic.exe");
                    break;

                case ResticExecutableSource.Custom:
                    if (string.IsNullOrWhiteSpace(config.CustomExecutablePath))
                    {
                        throw new InvalidOperationException("La ruta del ejecutable Restic personalizado no está configurada.");
                    }

                    path = config.CustomExecutablePath;
                    break;

                default:
                    throw new InvalidOperationException("La fuente del ejecutable Restic no es válida.");
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"No se encontró el ejecutable de Restic en: {path}", path);
            }

            return path;
        }
    }
}
