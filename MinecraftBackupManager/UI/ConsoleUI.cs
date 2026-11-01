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

    public void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Presiona ENTER para continuar...");
        Console.ReadLine();
    }
}
