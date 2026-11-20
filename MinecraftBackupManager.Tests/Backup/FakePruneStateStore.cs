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

        public int SuccessCount { get; private set; }

        public int FailureCount { get; private set; }

        public bool ShouldPrune()
        {
            _log.Add("prune-state:should-prune");

            return ShouldPruneResult;
        }

        public void RecordPruneSuccess()
        {
            _log.Add("prune-state:success");

            SuccessCount++;
        }

        public void RecordPruneFailure()
        {
            _log.Add("prune-state:failure");

            FailureCount++;
        }
    }
}
