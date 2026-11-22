namespace MinecraftBackupManager.Tests.Backup
{
    /// <summary>
    /// Lista compartida donde los sustitutos anotan, en orden, lo que se les pide.
    /// </summary>
    internal sealed class BackupCallLog
    {
        private readonly List<string> _entries = new();
        private readonly object _lock = new();

        public IReadOnlyList<string> Entries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToArray();
                }
            }
        }

        public void Add(string entry)
        {
            lock (_lock)
            {
                _entries.Add(entry);
            }
        }
    }
}
