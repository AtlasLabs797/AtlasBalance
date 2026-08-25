using Npgsql;

namespace AtlasBalance.API.Services;

public enum DatabaseTlsDecision
{
    Ok,
    InsecureRemote
}

public sealed record DatabaseTlsVerdict(DatabaseTlsDecision Decision, string? Host = null, SslMode Mode = SslMode.Disable);

// SEC V-02.09: antes de este tipo, una connection string contra un host remoto
// con SslMode=Disable/Prefer solo producia un warning en el arranque. El trafico
// con PostgreSQL lleva PII financiera; contra un host remoto sin cifrar queda a
// la merced de la red. La decision ahora es fail-closed: Program.cs bloquea el
// arranque salvo que el operador active explicitamente
// Security:AllowInsecureDatabaseTransport (escape para topologias justificadas).
// Localhost/loopback sigue exento: BD y API en la misma maquina no cruzan red.
public static class DatabaseTlsPolicy
{
    public static DatabaseTlsVerdict Evaluate(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return new DatabaseTlsVerdict(DatabaseTlsDecision.Ok);
        }

        NpgsqlConnectionStringBuilder parsed;
        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
        {
            // Cadena no parseable: no es este el sitio para validar su formato.
            // La conexion fallara por si sola mas adelante. KeyNotFoundException
            // la suelta el builder de Npgsql con keywords desconocidas.
            return new DatabaseTlsVerdict(DatabaseTlsDecision.Ok);
        }

        var host = parsed.Host?.Trim();
        var isLoopbackHost = string.IsNullOrEmpty(host) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host == "127.0.0.1" ||
            host == "::1";

        if (isLoopbackHost || (parsed.SslMode != SslMode.Disable && parsed.SslMode != SslMode.Prefer))
        {
            return new DatabaseTlsVerdict(DatabaseTlsDecision.Ok);
        }

        return new DatabaseTlsVerdict(DatabaseTlsDecision.InsecureRemote, host, parsed.SslMode);
    }
}
