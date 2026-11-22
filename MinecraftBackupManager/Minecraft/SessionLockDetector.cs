namespace MinecraftBackupManager.Minecraft
{
    /// <summary>
    /// Estado del archivo session.lock de un mundo de Minecraft.
    /// </summary>
    public enum SessionLockState
    {
        /// <summary>El archivo no está bloqueado, o no existe: no hay un servidor corriendo con ese mundo.</summary>
        Free,

        /// <summary>El archivo está bloqueado: hay un servidor encendido (o arrancando) con ese mundo.</summary>
        InUse
    }

    /// <summary>
    /// Averigua si un servidor Minecraft está usando un mundo, mirando su session.lock.
    /// </summary>
    public sealed class SessionLockDetector
    {
        public SessionLockState Check(string sessionLockPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionLockPath);

            if (!Path.IsPathFullyQualified(sessionLockPath))
            {
                throw new ArgumentException("La ruta de session.lock debe ser una ruta completa.", nameof(sessionLockPath));
            }

            try
            {
                // Se abre permitiendo que otros lo lean, escriban o borren: así lo abre
                // el servidor, y por eso abrirlo no basta para saber si está en uso.
                using var stream = new FileStream(
                    sessionLockPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);

                // Lo que el servidor bloquea es el contenido: mientras siga encendido,
                // leer un byte falla. Es lo mismo que comprobaste con PowerShell.
                stream.ReadByte();

                return SessionLockState.Free;
            }
            catch (FileNotFoundException)
            {
                return SessionLockState.Free;
            }
            catch (DirectoryNotFoundException)
            {
                return SessionLockState.Free;
            }
            catch (IOException)
            {
                // Bloqueo o violación de uso compartido: el servidor lo tiene en uso.
                return SessionLockState.InUse;
            }
        }
    }
}
