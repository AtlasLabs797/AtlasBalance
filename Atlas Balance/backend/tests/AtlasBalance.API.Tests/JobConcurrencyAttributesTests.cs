using AtlasBalance.API.Jobs;
using FluentAssertions;
using Hangfire;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// V-03.02 (J-1): con 2 workers de Hangfire, un job sin
// DisableConcurrentExecution puede solaparse consigo mismo y con otro job
// del mismo worker, y sin AutomaticRetry un fallo transitorio (SMTP caido,
// base de datos reiniciandose) se queda sin reintentar. Un backup largo mas
// una exportacion retrasaban las alertas de seguridad.
//
// Aqui solo se comprueban los jobs de este lote (exportacion mensual,
// limpiezas y alertas). Los de backup, limpieza de exportaciones y refresh
// tokens tienen su propio fichero de test.
// -----------------------------------------------------------------------
public sealed class JobConcurrencyAttributesTests
{
    public static TheoryData<Type, int> JobsDelLote3() => new()
    {
        // El timeout debe ser mayor que la duracion real del job: el backup
        // lleva 3600 porque pg_dump tiene timeout de 30 minutos.
        { typeof(ExportMensualJob), 1800 },
        { typeof(LimpiezaAuditoriaJob), 900 },
        { typeof(LimpiezaMfaTrustedDevicesJob), 900 },
        { typeof(LimpiezaImportacionLotesJob), 900 },
        { typeof(SecurityAlertJob), 600 }
    };

    [Theory]
    [MemberData(nameof(JobsDelLote3))]
    public void Job_Should_Block_Concurrent_Executions(Type tipoJob, int timeoutEsperado)
    {
        var atributo = tipoJob
            .GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false)
            .Cast<DisableConcurrentExecutionAttribute>()
            .SingleOrDefault();

        atributo.Should().NotBeNull();
        atributo!.TimeoutSec.Should().Be(timeoutEsperado);
    }

    [Theory]
    [MemberData(nameof(JobsDelLote3))]
    public void Job_Should_Retry_Automatically(Type tipoJob, int timeoutEsperado)
    {
        var atributo = tipoJob
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .SingleOrDefault();

        atributo.Should().NotBeNull();
        atributo!.Attempts.Should().Be(3);
        atributo.DelaysInSeconds.Should().Equal(60, 300, 900);
    }
}
