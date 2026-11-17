using System.Security.Cryptography;
using System.Text.Json;
using MinecraftBackupManager.Infrastructure;
using MinecraftBackupManager.Security.Models;

namespace MinecraftBackupManager.Security
{
    public sealed class CredentialManager
    {
        private readonly ApplicationPaths _paths;

        public CredentialManager(ApplicationPaths paths)
        {
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public void Save(CredentialsData credentials)
        {
            ArgumentNullException.ThrowIfNull(credentials);

            byte[] serializedData = JsonSerializer.SerializeToUtf8Bytes(credentials);
            byte[] protectedData;

            try
            {
                protectedData = ProtectedData.Protect(serializedData, optionalEntropy: null, DataProtectionScope.CurrentUser);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(serializedData);
            }


            try
            {
                _paths.EnsureDirectoriesExist();

                string temporaryFile = _paths.CredentialsFile + ".tmp";

                try
                {
                    using (FileStream stream = new(temporaryFile, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(protectedData);
                    }

                    File.Move(temporaryFile, _paths.CredentialsFile, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporaryFile))
                    {
                        File.Delete(temporaryFile);
                    }
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedData);
            }
        }

        public CredentialsData? Load()
        {
            if (!File.Exists(_paths.CredentialsFile))
            {
                return null;
            }

            byte[] protectedData = File.ReadAllBytes(_paths.CredentialsFile);
            byte[] decryptedData;

            try
            {
                decryptedData = ProtectedData.Unprotect(protectedData, optionalEntropy: null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException("No se pudieron descifrar las credenciales.\nComprueba que estás utilizando la misma cuenta de Windows\ncon la que fueron protegidas y que el archivo no está dañado.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(protectedData);
            }

            try
            {
                CredentialsData? credentials = JsonSerializer.Deserialize<CredentialsData>(decryptedData);

                if (credentials is null)
                {
                    throw new InvalidDataException("El archivo de credenciales no contiene datos válidos.");
                }

                return credentials;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("El archivo de credenciales descifrado no contiene datos válidos.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(decryptedData);
            }
        }

        public bool Exists()
        {
            return File.Exists(_paths.CredentialsFile);
        }

        public void Delete()
        {
            if (File.Exists(_paths.CredentialsFile))
            {
                File.Delete(_paths.CredentialsFile);
            }
        }
    }
}
