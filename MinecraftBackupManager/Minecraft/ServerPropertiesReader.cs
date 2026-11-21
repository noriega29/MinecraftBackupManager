using System.Globalization;
using System.Text;

namespace MinecraftBackupManager.Minecraft
{
    /// <summary>
    /// Lee datos del archivo server.properties de un servidor Minecraft.
    /// </summary>
    public sealed class ServerPropertiesReader
    {
        public const string DefaultLevelName = "world";

        private const string FileName = "server.properties";
        private const string LevelNameKey = "level-name";

        /// <summary>
        /// Devuelve el nombre de la carpeta del mundo (campo level-name). Si el
        /// campo no aparece o está vacío, devuelve el valor por defecto de Minecraft.
        /// </summary>
        public string ReadLevelName(string serverDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(serverDirectory);

            if (!Path.IsPathFullyQualified(serverDirectory))
            {
                throw new ArgumentException("La ruta del servidor debe ser una ruta completa.", nameof(serverDirectory));
            }

            string filePath = Path.Combine(serverDirectory, FileName);

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"No se encontró server.properties en la carpeta del servidor: {serverDirectory}", filePath);
            }

            string? levelName = null;

            foreach (string line in ReadAllLines(filePath))
            {
                if (TryParseLine(line, out string key, out string value) && key == LevelNameKey)
                {
                    // Si el campo aparece varias veces, gana la última (como en Java).
                    levelName = value;
                }
            }

            levelName = levelName?.Trim();

            return string.IsNullOrEmpty(levelName) ? DefaultLevelName : levelName;
        }

        // Se abre permitiendo que otros procesos lo lean o lo escriban a la vez:
        // el usuario puede tener el archivo abierto en el Bloc de notas.
        private static string[] ReadAllLines(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            var lines = new List<string>();
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                lines.Add(line);
            }

            return lines.ToArray();
        }

        // Separa una línea "clave=valor". Devuelve false para líneas vacías y comentarios.
        private static bool TryParseLine(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;

            string text = line.TrimStart();

            if (text.Length == 0 || text[0] is '#' or '!')
            {
                return false;
            }

            int index = 0;

            // La clave termina en el primer '=', ':' o espacio que no esté escapado.
            while (index < text.Length)
            {
                char current = text[index];

                if (current == '\\')
                {
                    index += 2;
                    continue;
                }

                if (current is '=' or ':' or ' ' or '\t')
                {
                    break;
                }

                index++;
            }

            int keyEnd = Math.Min(index, text.Length);

            key = Unescape(text[..keyEnd]);

            index = keyEnd;

            // Espacios, un separador opcional ('=' o ':') y más espacios.
            while (index < text.Length && (text[index] is ' ' or '\t'))
            {
                index++;
            }

            if (index < text.Length && (text[index] is '=' or ':'))
            {
                index++;
            }

            while (index < text.Length && (text[index] is ' ' or '\t'))
            {
                index++;
            }

            value = Unescape(text[index..]);

            return true;
        }

        // Convierte las secuencias con barra invertida: \u00ed pasa a ser "í", \\ pasa a ser "\", etc.
        private static string Unescape(string text)
        {
            if (!text.Contains('\\'))
            {
                return text;
            }

            var result = new StringBuilder(text.Length);

            for (int index = 0; index < text.Length; index++)
            {
                char current = text[index];

                // Una barra final sin nada detrás se conserva tal cual.
                if (current != '\\' || index == text.Length - 1)
                {
                    result.Append(current);
                    continue;
                }

                index++;

                char escaped = text[index];

                switch (escaped)
                {
                    case 'u' when TryReadHex(text, index + 1, out char unicode):
                        result.Append(unicode);
                        index += 4;
                        break;

                    case 't':
                        result.Append('\t');
                        break;

                    case 'n':
                        result.Append('\n');
                        break;

                    case 'r':
                        result.Append('\r');
                        break;

                    case 'f':
                        result.Append('\f');
                        break;

                    default:
                        result.Append(escaped);
                        break;
                }
            }

            return result.ToString();
        }

        private static bool TryReadHex(string text, int start, out char value)
        {
            value = '\0';

            if (start + 4 > text.Length)
            {
                return false;
            }

            if (!int.TryParse(text.AsSpan(start, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code))
            {
                return false;
            }

            value = (char)code;

            return true;
        }
    }
}
