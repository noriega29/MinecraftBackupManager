namespace MinecraftBackupManager.Configuration.Models
{
    /// <summary>
    /// Config es una copia con el cambio aplicado: solo debe usarse si se aceptó.
    /// </summary>
    public sealed record ConfigurationChangeResult(
        ApplicationConfig Config,
        IReadOnlyList<string> Errors)
    {
        public bool IsAccepted => Errors.Count == 0;
    }
}
