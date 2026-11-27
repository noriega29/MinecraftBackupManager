using MinecraftBackupManager.Backup;
using MinecraftBackupManager.Configuration.Models;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Minecraft;
using MinecraftBackupManager.Restic;
using MinecraftBackupManager.Security;

namespace MinecraftBackupManager.Core;

/// <summary>
/// Agrupa las piezas que dependen de la configuración. Para aplicar una
/// configuración nueva se libera este objeto y se crea otro.
/// </summary>
public sealed class BackupServices : IAsyncDisposable
{
    private readonly MinecraftServer _server;
    private readonly object _disposeLock = new();

    private Task? _disposeTask;

    public BackupManager Manager { get; }

    public BackupScheduler Scheduler { get; }

    public ResticClient Repository { get; }

    /// <summary>
    /// Construye las piezas a partir de una configuración ya validada. No conecta
    /// con el servidor, no ejecuta Restic y no arranca el planificador.
    /// </summary>
    public BackupServices(
        ApplicationConfig config,
        ApplicationPaths paths,
        LogManager logManager,
        CredentialManager credentialManager)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(logManager);
        ArgumentNullException.ThrowIfNull(credentialManager);

        _server = new MinecraftServer(config.MinecraftServer, credentialManager, logManager);

        Repository = new ResticClient(
            config.Restic,
            credentialManager,
            new ResticExecutableLocator(),
            new ResticProcessRunner(),
            logManager);

        Manager = new BackupManager(
            _server,
            Repository,
            new PruneStateStore(paths, logManager),
            new WorldLockProbe(config.MinecraftServer),
            config.MinecraftServer,
            config.Retention,
            logManager);

        Scheduler = new BackupScheduler(Manager, Repository, new BackupKindSelector(), logManager);
    }

    /// <summary>
    /// Cierra todo en orden. Si se llama varias veces, todas las llamadas
    /// esperan al mismo cierre.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            _disposeTask ??= DisposeCoreAsync();

            return new ValueTask(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        // 1. Que no empiecen más respaldos automáticos.
        await Scheduler.DisposeAsync();

        // 2. Cancelar cualquier otro respaldo y esperar a que termine.
        await Manager.StopAsync();

        // 3. Al final el servidor: reintenta "save-on" si quedó pendiente.
        await _server.DisposeAsync();
    }
}
