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
// Se recorren por reflexion TODAS las clases de AtlasBalance.API.Jobs: un job
// nuevo sin ambos atributos hace fallar este test. Ademas se fijan los
// timeouts y reintentos de los jobs del lote 3.
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

    private static IEnumerable<Type> TodosLosJobs() => typeof(ExportMensualJob).Assembly
        .GetTypes()
        .Where(t => t.IsClass && !t.IsNested && t.Namespace == "AtlasBalance.API.Jobs"
            && !t.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), inherit: false));

    [Fact]
    public void Todos_Los_Jobs_Deben_Tener_DisableConcurrentExecution_Y_AutomaticRetry()
    {
        var jobs = TodosLosJobs().ToList();
        jobs.Count.Should().BeGreaterOrEqualTo(15);

        var sinConcurrencia = jobs
            .Where(t => !t.IsDefined(typeof(DisableConcurrentExecutionAttribute), inherit: false))
            .Select(t => t.Name)
            .ToList();
        var sinReintento = jobs
            .Where(t => !t.IsDefined(typeof(AutomaticRetryAttribute), inherit: false))
            .Select(t => t.Name)
            .ToList();

        sinConcurrencia.Should().BeEmpty("todo job necesita DisableConcurrentExecution");
        sinReintento.Should().BeEmpty("todo job necesita AutomaticRetry (aunque sea Attempts = 0)");
    }

    [Fact]
    public void AutoUpdateJob_No_Debe_Reintentar_Automaticamente()
    {
        // Aplicar una actualizacion reinicia la app: un reintento podria relanzarla.
        var retry = typeof(AutoUpdateJob)
            .GetCustomAttributes(typeof(AutomaticRetryAttribute), inherit: false)
            .Cast<AutomaticRetryAttribute>()
            .Single();
        retry.Attempts.Should().Be(0);
    }

    [Theory]
    [InlineData(typeof(SyncTiposCambioJob), 600)]
    [InlineData(typeof(PlazoFijoVencimientoJob), 900)]
    [InlineData(typeof(VerificacionIntegridadAuditoriaJob), 1800)]
    [InlineData(typeof(HealthAlertJob), 240)]
    [InlineData(typeof(AutoUpdateJob), 600)]
    public void Jobs_Del_Lote_Complementario_Tienen_Timeout_Coherente_Con_Su_Cron(Type tipoJob, int timeoutEsperado)
    {
        var atributo = tipoJob
            .GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: false)
            .Cast<DisableConcurrentExecutionAttribute>()
            .Single();
        atributo.TimeoutSec.Should().Be(timeoutEsperado);
    }
}
