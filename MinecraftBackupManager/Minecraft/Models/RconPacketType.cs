public enum RconPacketType
{
    ResponseValue = 0,
    // El protocolo RCON usa el valor 2 para ambos: ExecuteCommand lo envía
    // el cliente y AuthenticationResponse lo envía el servidor.
    AuthenticationResponse = 2,
    ExecuteCommand = 2,
    Authentication = 3
}
