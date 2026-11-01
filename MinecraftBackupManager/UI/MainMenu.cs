namespace MinecraftBackupManager.UI;

public enum MainMenuOption
{
    Invalid,
    Configuration,
    Backups,
    Restorations,
    Information,
    Exit
}

public sealed class MainMenu
{
    private readonly ConsoleUI _console;

    public MainMenu(ConsoleUI console)
    {
        _console = console;
    }

    public MainMenuOption Show()
    {
        _console.ShowHeader("MINECRAFT BACKUP MANAGER");

        _console.WriteLine("1. Configuración");
        _console.WriteLine("2. Copias de seguridad");
        _console.WriteLine("3. Restauraciones");
        _console.WriteLine("4. Informacion de la aplicacion");
        _console.WriteLine("0. Salir");

        _console.WriteLine();

        string input = _console.ReadInput("Seleccione una opción: ");

        return input switch
        {
            "1" => MainMenuOption.Configuration,
            "2" => MainMenuOption.Backups,
            "3" => MainMenuOption.Restorations,
            "4" => MainMenuOption.Information,
            "0" => MainMenuOption.Exit,
            _ => MainMenuOption.Invalid
        };
    }
}