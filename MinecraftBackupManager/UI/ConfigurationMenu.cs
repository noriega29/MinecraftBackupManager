namespace MinecraftBackupManager.UI;

public sealed class ConfigurationMenu
{
    private readonly ConsoleUI _console;

    public ConfigurationMenu(ConsoleUI console)
    {
        _console = console;
    }

    public void Run()
    {
        _console.Clear();
        _console.ShowHeader("CONFIGURACIÓN");

        _console.WriteLine("Esta seccion se encuentra en desarrollo.");

        _console.Pause();
    }
}
