namespace MinecraftBackupManager.UI;

public sealed class BackupMenu
{
    private readonly ConsoleUI _console;

    public BackupMenu(ConsoleUI console)
    {
        _console = console;
    }

    public void Run()
    {
        _console.Clear();
        _console.ShowHeader("COPIAS DE SEGURIDAD");

        _console.WriteLine("Esta seccion se encuentra en desarrollo.");

        _console.Pause();
    }
}
