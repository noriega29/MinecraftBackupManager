namespace MinecraftBackupManager.Configuration.Models
{
    /// <summary>
    /// Resultado de revisar la configuración. Config es null si no se pudo cargar.
    /// </summary>
    public sealed record SetupStatus(
        SetupState State,
        ApplicationConfig? Config,
        IReadOnlyList<string> Errors
    );
}
