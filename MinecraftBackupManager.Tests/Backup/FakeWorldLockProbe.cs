using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Minecraft;

namespace MinecraftBackupManager.Tests.Backup
{
    internal sealed class FakeWorldLockProbe : IWorldLockProbe
    {
        private readonly BackupCallLog _log;
        private int _calls;

        public FakeWorldLockProbe(BackupCallLog log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        // Lo que devolverá CheckWorldLock cuando la cola esté vacía.
        public SessionLockState State { get; set; } = SessionLockState.Free;

        // Respuestas que se devuelven primero, una por llamada. Sirve para simular
        // un servidor que está apagado al principio y se enciende después.
        public Queue<SessionLockState> QueuedStates { get; } = new();

        // Si se asigna, CheckWorldLock falla con esta excepción.
        public Exception? CheckException { get; set; }

        // Si es 0, CheckException se lanza en todas las llamadas. Si es N, solo
        // desde la llamada número N (la primera es la 1).
        public int FailFromCall { get; set; }

        public SessionLockState CheckWorldLock()
        {
            _log.Add("world-lock");

            _calls++;

            if (CheckException is not null && (FailFromCall == 0 || _calls >= FailFromCall))
            {
                throw CheckException;
            }

            return QueuedStates.Count > 0 ? QueuedStates.Dequeue() : State;
        }
    }
}
