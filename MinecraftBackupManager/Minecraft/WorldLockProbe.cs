using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Configuration.Models;

namespace MinecraftBackupManager.Minecraft
{
    /// <summary>
    /// Implementa IWorldLockProbe para el servidor configurado: calcula la ruta de
    /// session.lock y comprueba si el servidor lo tiene bloqueado.
    /// </summary>
    public sealed class WorldLockProbe : IWorldLockProbe
    {
        private readonly MinecraftServerConfig _config;
        private readonly SessionLockLocator _locator;
        private readonly SessionLockDetector _detector;

        public WorldLockProbe(MinecraftServerConfig config)
            : this(config, new SessionLockLocator(), new SessionLockDetector()) { }

        public WorldLockProbe(MinecraftServerConfig config, SessionLockLocator locator, SessionLockDetector detector)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _locator = locator ?? throw new ArgumentNullException(nameof(locator));
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        }

        public SessionLockState CheckWorldLock()
        {
            // La carpeta del servidor se lee en cada llamada, sin guardarla,
            // por si la configuración cambia mientras la aplicación está abierta.
            string sessionLockPath = _locator.Resolve(_config.ServerDirectory);

            return _detector.Check(sessionLockPath);
        }
    }
}
