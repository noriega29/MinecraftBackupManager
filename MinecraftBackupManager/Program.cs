using MinecraftBackupManager.Core;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.UI;

try
{
    // "using" mantiene la marca mientras se ejecuta este bloque.
    using var instanceGuard = new SingleInstanceGuard();

    if (!instanceGuard.IsFirstInstance)
    {
        var console = new ConsoleUI();

        console.WriteLine("La aplicación ya está abierta.");
        console.Pause();

        return;
    }

    var application = new Application();
    await application.RunAsync();
}
catch (Exception exception)
{
    var console = new ConsoleUI();

    console.WriteLine();
    console.WriteLine("La aplicación no pudo continuar:");
    console.WriteLine(exception.Message);
    console.Pause();
}
