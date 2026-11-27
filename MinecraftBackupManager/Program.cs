using MinecraftBackupManager.Core;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.UI;

using var instanceGuard = new SingleInstanceGuard();

if (!instanceGuard.IsFirstInstance)
{
    var console = new ConsoleUI();

    console.WriteLine("La aplicación ya está abierta.");
    console.Pause();

    return;
}

var application = new Application();
application.Run();
