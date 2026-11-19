namespace MinecraftBackupManager.Restic.Models
{
    public sealed record ProcessExecutionResult(
        int ExitCode,
        string StandardOutput,
        string StandardError
    );
}
