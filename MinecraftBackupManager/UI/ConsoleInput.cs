using System.Text;

namespace MinecraftBackupManager.UI
{
    /// <summary>
    /// Tratamiento de lo que escribe el usuario, separado de la consola para poder probarlo.
    /// </summary>
    internal static class ConsoleInput
    {
        // Quita los espacios y las comillas que añade "Copiar como ruta" de Windows.
        public static string CleanPath(string input)
        {
            ArgumentNullException.ThrowIfNull(input);

            string path = input.Trim();

            if (path.Length >= 2 && path.StartsWith('"') && path.EndsWith('"'))
            {
                path = path[1..^1].Trim();
            }

            return path;
        }

        // Lee tecla a tecla hasta ENTER y muestra un asterisco por cada carácter.
        public static string ReadMasked(Func<ConsoleKeyInfo> readKey, Action<string> write)
        {
            var text = new StringBuilder();

            while (true)
            {
                ConsoleKeyInfo key = readKey();

                if (key.Key == ConsoleKey.Enter)
                {
                    return text.ToString();
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (text.Length > 0)
                    {
                        text.Length--;

                        // Retrocede, borra el asterisco y vuelve a retroceder.
                        write("\b \b");
                    }
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    text.Append(key.KeyChar);
                    write("*");
                }
            }
        }
    }
}
