using MinecraftBackupManager.UI;

namespace MinecraftBackupManager.Core;

public sealed class Application
{
    private readonly ConsoleUI _console;
    private readonly MainMenu _mainMenu;
    private readonly ConfigurationMenu _configurationMenu;
    private readonly BackupMenu _backupMenu;
    private readonly RestorationMenu _restorationMenu;
    private readonly InformationMenu _informationMenu;

    public Application()
    {
        _console = new ConsoleUI();

        _mainMenu = new MainMenu(_console);
        _configurationMenu = new ConfigurationMenu(_console);
        _backupMenu = new BackupMenu(_console);
        _restorationMenu = new RestorationMenu(_console);
        _informationMenu = new InformationMenu(_console);
    }

    public void Run()
    {
        bool isRunning = true;

        while (isRunning)
        {
            _console.Clear();

            MainMenuOption option = _mainMenu.Show();

            switch (option)
            {
                case MainMenuOption.Configuration:
                    _configurationMenu.Run();
                    break;

                case MainMenuOption.Backups:
                    _backupMenu.Run();
                    break;

                case MainMenuOption.Restorations:
                    _restorationMenu.Run();
                    break;

                case MainMenuOption.Information:
                    _informationMenu.Run();
                    break;

                case MainMenuOption.Exit:
                    isRunning = false;
                    break;

                case MainMenuOption.Invalid:
                    _console.WriteLine();
                    _console.WriteLine("Opción inválida.");
                    _console.Pause();
                    break;
            }
        }

        _console.Clear();
        _console.WriteLine("Gracias por usar Minecraft Backup Manager.");
        _console.WriteLine("Minecraft Backup Manager se ha cerrado.");
    }
}
