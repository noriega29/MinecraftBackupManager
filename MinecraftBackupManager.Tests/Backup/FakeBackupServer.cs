using MinecraftBackupManager.Backup;

namespace MinecraftBackupManager.Tests.Backup
{
    internal sealed class FakeBackupServer : IBackupServer
    {
        private readonly BackupCallLog _log;

        public FakeBackupServer(BackupCallLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public bool IsAutosaveDisabled { get; private set; }

        // Si se asigna, ResumeAutosaveAsync falla con esta excepción y el
        // guardado queda "desactivado", como pasaría si el servidor no responde.
        public Exception? ResumeException { get; set; }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("connect");

            return Task.CompletedTask;
        }

        public Task PrepareForBackupAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("prepare");

            IsAutosaveDisabled = true;

            return Task.CompletedTask;
        }

        public Task ResumeAutosaveAsync(CancellationToken cancellationToken = default)
        {
            _log.Add("resume");

            if (ResumeException is not null)
            {
                return Task.FromException(ResumeException);
            }

            IsAutosaveDisabled = false;

            return Task.CompletedTask;
        }
    }
}
