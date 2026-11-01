namespace MinecraftBackupManager.UI;

public sealed class InformationMenu
{
    private readonly ConsoleUI _console;

    public InformationMenu(ConsoleUI console)
    {
        _console = console;
    }

    public void Run()
    {
        _console.Clear();
        _console.ShowHeader("INFORMACION DE LA APLICACION");

        _console.WriteLine("Minecraft Backup Manager");
        _console.WriteLine("Version: 0.1.0");
        _console.WriteLine("Desarrollado por: Mateo Noriega");
        _console.WriteLine();
        _console.WriteLine("Administrador de copias de seguridad");
        _console.WriteLine("y restauraciones para servidores Minecraft.");

        _console.Pause();
    }
}
