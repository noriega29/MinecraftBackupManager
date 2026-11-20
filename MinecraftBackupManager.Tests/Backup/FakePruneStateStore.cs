using MinecraftBackupManager.Backup;

namespace MinecraftBackupManager.Tests.Backup
{
    internal sealed class FakePruneStateStore : IPruneStateStore
    {
        private readonly BackupCallLog _log;

        public FakePruneStateStore(BackupCallLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // Lo que devolverá ShouldPrune.
        public bool ShouldPruneResult { get; set; }

        // Si se asigna, ShouldPrune falla con esta excepción.
        public Exception? ShouldPruneException { get; set; }

        // Si se asigna, RecordPruneSuccess y RecordPruneFailure fallan con esta excepción.
        public Exception? RecordException { get; set; }

        public int SuccessCount { get; private set; }

        public int FailureCount { get; private set; }

        public bool ShouldPrune()
        {
            _log.Add("prune-state:should-prune");

            if (ShouldPruneException is not null)
            {
                throw ShouldPruneException;
            }

            return ShouldPruneResult;
        }

        public void RecordPruneSuccess()
        {
            _log.Add("prune-state:success");

            if (RecordException is not null)
            {
                throw RecordException;
            }

            SuccessCount++;
        }

        public void RecordPruneFailure()
        {
            _log.Add("prune-state:failure");

            if (RecordException is not null)
            {
                throw RecordException;
            }

            FailureCount++;
        }
    }
}
