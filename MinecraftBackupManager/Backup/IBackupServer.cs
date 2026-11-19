namespace MinecraftBackupManager.Backup
{
    /// <summary>
    /// Operaciones del servidor Minecraft que necesita un respaldo.
    /// </summary>
    public interface IBackupServer
    {
        /// <summary>Indica si el guardado automático quedó desactivado y sin reactivar.</summary>
        bool IsAutosaveDisabled { get; }

        Task ConnectAsync(CancellationToken cancellationToken = default);

        Task PrepareForBackupAsync(CancellationToken cancellationToken = default);

        Task ResumeAutosaveAsync(CancellationToken cancellationToken = default);
    }
}
