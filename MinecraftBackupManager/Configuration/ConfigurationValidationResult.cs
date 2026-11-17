namespace MinecraftBackupManager.Configuration
{
    public sealed class ConfigurationValidationResult
    {
        public IReadOnlyList<string> Errors { get; }

        public bool IsValid => Errors.Count == 0;

        public ConfigurationValidationResult(IReadOnlyList<string> errors)
        {
            Errors = errors.ToArray();
        }
    }
}
