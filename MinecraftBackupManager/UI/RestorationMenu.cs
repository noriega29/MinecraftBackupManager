namespace MinecraftBackupManager.UI;

public sealed class RestorationMenu
{
    private readonly ConsoleUI _console;
    
    public RestorationMenu(ConsoleUI console)
    {
        _console = console;
    }
    
    public void Run()
    {
        _console.Clear();
        _console.ShowHeader("RESTAURACIONES");

        _console.WriteLine("Esta seccion se encuentra en desarrollo.");

        _console.Pause();
    }
}
