namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Registro del estado de la compactación (prune) del repositorio,
    /// pensado para sobrevivir a reinicios de la aplicación.
    /// </summary>
    public interface IPruneStateStore
    {
        /// <summary>
        /// Indica si corresponde ejecutar prune ahora: hoy aún no hubo uno
        /// exitoso y los intentos fallidos de hoy no superan los reintentos
        /// extra permitidos.
        /// </summary>
        bool ShouldPrune();

        /// <summary>Anota que el prune de hoy terminó con éxito.</summary>
        void RecordPruneSuccess();

        /// <summary>Anota un intento fallido de prune en el día de hoy.</summary>
        void RecordPruneFailure();
    }
}
