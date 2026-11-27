namespace MinecraftBackupManager.UI;

public sealed class ConsoleUI
{
    private const string Separator = "========================================";

    public void Clear()
    {
        Console.Clear();
    }

    public void ShowHeader(string title)
    {
        Console.WriteLine(Separator);
        Console.WriteLine($"       {title} ");
        Console.WriteLine(Separator);
        Console.WriteLine();
    }

    public void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }

    public string ReadInput(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine()?.Trim() ?? string.Empty;
    }

    // El texto de "label" va sin dos puntos: se añaden después del valor por defecto.
    public string ReadWithDefault(string label, string defaultValue)
    {
        string input = ReadInput($"{label} [{defaultValue}]: ");

        return input.Length == 0 ? defaultValue : input;
    }

    public string ReadPath(string prompt)
    {
        return ConsoleInput.CleanPath(ReadInput(prompt));
    }

    // No recorta espacios: pueden formar parte de la contraseña.
    public string ReadPassword(string prompt)
    {
        Console.Write(prompt);

        // Con la entrada redirigida no se puede leer tecla a tecla.
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? string.Empty;
        }

        string password = ConsoleInput.ReadMasked(() => Console.ReadKey(intercept: true), Console.Write);

        Console.WriteLine();

        return password;
    }

    public void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Presiona ENTER para continuar...");
        Console.ReadLine();
    }
}
