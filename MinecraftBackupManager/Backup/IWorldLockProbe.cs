using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Averigua, sin usar RCON, si un servidor Minecraft está usando su mundo.
    /// </summary>
    public interface IWorldLockProbe
    {
        /// <summary>
        /// Devuelve Free si ningún servidor está usando el mundo (servidor apagado)
        /// o InUse si lo está usando. Lanza una excepción con un mensaje claro si no
        /// puede determinarlo: falta server.properties, no existe la carpeta del mundo,
        /// o el nombre del mundo apunta fuera de la carpeta del servidor.
        /// </summary>
        SessionLockState CheckWorldLock();
    }
}
