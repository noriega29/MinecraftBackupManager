namespace MinecraftBackupManager.Infrastructure
{
    /// <summary>
    /// Permite saber si la aplicación ya está abierta en esta sesión de Windows.
    /// La marca existe mientras algún objeto de esta clase siga sin liberarse.
    /// </summary>
    public sealed class SingleInstanceGuard : IDisposable
    {
        // "Local\" limita la marca a la sesión de Windows actual.
        private const string DefaultName = @"Local\MinecraftBackupManager";

        private readonly Mutex _mutex;

        /// <summary>Indica si no había otra instancia abierta al crear este objeto.</summary>
        public bool IsFirstInstance { get; }

        public SingleInstanceGuard() : this(DefaultName) { }

        // Constructor destinado a las pruebas automatizadas.
        internal SingleInstanceGuard(string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            // No se toma el mutex: basta con saber si ya existía.
            _mutex = new Mutex(initiallyOwned: false, name, out bool createdNew);

            IsFirstInstance = createdNew;
        }

        public void Dispose()
        {
            _mutex.Dispose();
        }
    }
}
