using Npgsql;
using Xunit;

namespace AtlasBalance.API.Tests.Rls;

// V-03.01 (#7): se decidio NO cachear el contexto RLS por DbConnection (ver
// informe de la tarea) precisamente porque esa optimizacion solo es segura si
// Npgsql limpia el set_config de sesion al devolver la conexion al pool. Este
// test deja esa precondicion como regresion permanente, no como medicion
// temporal: si algun cambio futuro (p.ej. anadir "No Reset On Close=true" a la
// cadena de conexion por rendimiento) rompe esta garantia, cualquier cache de
// contexto RLS por conexion se volveria insegura (una conexion reciclada
// heredaria el contexto del usuario anterior) y este test debe fallar primero.
[Trait("Category", "Postgres")]
[Collection(PostgresCollection.Name)]
public sealed class RlsPoolResetProbe
{
    private readonly PostgresFixture _fixture;

    public RlsPoolResetProbe(PostgresFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Npgsql_Default_Pooling_Should_Reset_SessionLevel_SetConfig_On_Return_To_Pool()
    {
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            MaxPoolSize = 1,
            MinPoolSize = 1
        };
        var connectionString = builder.ConnectionString;

        int firstBackendPid;
        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            firstBackendPid = connection.ProcessID;
            await using var setCommand = connection.CreateCommand();
            setCommand.CommandText = "SELECT set_config('atlas.user_id', 'leaked-value', false)";
            await setCommand.ExecuteNonQueryAsync();
        } // Dispose -> vuelve al pool (Pooling=true por defecto, No Reset On Close no fijado -> false).

        await using var reused = new NpgsqlConnection(connectionString);
        await reused.OpenAsync();
        var secondBackendPid = reused.ProcessID;
        await using var readCommand = reused.CreateCommand();
        readCommand.CommandText = "SELECT current_setting('atlas.user_id', true)";
        var value = (string?)await readCommand.ExecuteScalarAsync();

        // Si MaxPoolSize=1 no logro forzar la reutilizacion del mismo backend,
        // el resultado no dice nada sobre el reset: lo hacemos explicito en
        // vez de dar un falso positivo de seguridad.
        Assert.True(firstBackendPid == secondBackendPid, "el test necesita que se reutilice la misma conexion fisica para ser concluyente");
        Assert.True(
            string.IsNullOrEmpty(value),
            $"Npgsql deberia limpiar 'atlas.user_id' al devolver la conexion al pool; valor encontrado: '{value}'");
    }
}
