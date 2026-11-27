namespace MinecraftBackupManager.Configuration.Models
{
    public enum SetupState
    {
        NotConfigured,
        ConfigurationDamaged,
        ConfigurationUnsupported,
        ConfigurationInvalid,
        CredentialsMissing,
        CredentialsUnreadable,
        Ready
    }
}
