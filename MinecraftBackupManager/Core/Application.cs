using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Logging;
using MinecraftBackupManager.Logging.Models;
using MinecraftBackupManager.UI;

namespace MinecraftBackupManager.Core;

public sealed class Application
{
    private readonly LogManager _logManager;
    private readonly ConsoleUI _console;
    private readonly MainMenu _mainMenu;
    private readonly ConfigurationMenu _configurationMenu;
    private readonly BackupMenu _backupMenu;
    private readonly RestorationMenu _restorationMenu;
    private readonly InformationMenu _informationMenu;

    public Application()
    {
        var paths = new ApplicationPaths();

        _logManager = new LogManager(paths);
        _console = new ConsoleUI();

        _mainMenu = new MainMenu(_console);
        _configurationMenu = new ConfigurationMenu(_console);
        _backupMenu = new BackupMenu(_console);
        _restorationMenu = new RestorationMenu(_console);
        _informationMenu = new InformationMenu(_console);
    }

    public async Task RunAsync()
    {
        bool isRunning = true;

        while (isRunning)
        {
            _console.Clear();

            MainMenuOption option = _mainMenu.Show();

            switch (option)
            {
                case MainMenuOption.Exit:
                    isRunning = false;
                    break;

                case MainMenuOption.Invalid:
                    _console.WriteLine();
                    _console.WriteLine("Opción inválida.");
                    _console.Pause();
                    break;

                default:
                    await RunOptionSafelyAsync(option);
                    break;
            }
        }

        _console.Clear();
        _console.WriteLine("Gracias por usar Minecraft Backup Manager.");
        _console.WriteLine("Minecraft Backup Manager se ha cerrado.");
    }

    // Un error en una sección no cierra la aplicación: se muestra y se vuelve al menú.
    private async Task RunOptionSafelyAsync(MainMenuOption option)
    {
        try
        {
            await RunOptionAsync(option);
        }
        catch (OperationCanceledException)
        {
            _console.WriteLine();
            _console.WriteLine("Operación cancelada.");
            _console.Pause();
        }
        catch (Exception exception)
        {
            ShowError(option, exception);
        }
    }

    // Por ahora los menús son síncronos. Cada uno se esperará aquí cuando pase a ser asíncrono.
    private Task RunOptionAsync(MainMenuOption option)
    {
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

            default:
                throw new ArgumentOutOfRangeException(nameof(option), option, "La opción del menú no es válida.");
        }

        return Task.CompletedTask;
    }

    private void ShowError(MainMenuOption option, Exception exception)
    {
        // exception.ToString() incluye el tipo de error y dónde ocurrió.
        bool logged = TryLogError($"Error en la opción {option}: {exception}");

        _console.WriteLine();
        _console.WriteLine("No se pudo completar la operación:");
        _console.WriteLine(exception.Message);
        _console.WriteLine();
        _console.WriteLine(logged
            ? "Los detalles están en application.log."
            : "Además, no se pudo escribir en application.log.");

        _console.Pause();
    }

    private bool TryLogError(string message)
    {
        try
        {
            _logManager.Error(LogCategory.Application, message);

            return true;
        }
        catch
        {
            return false;
        }
    }
}
