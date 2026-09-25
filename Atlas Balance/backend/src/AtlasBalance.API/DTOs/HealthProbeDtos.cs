namespace AtlasBalance.API.DTOs;

/// <summary>
/// Respuesta minima de las sondas publicas. Los detalles de salud solo se
/// sirven mediante el endpoint administrativo /api/sistema/salud.
/// </summary>
internal sealed record HealthStatusResponse(string Status);

internal static class HealthProbeResponses
{
    public static HealthStatusResponse Liveness() => new("healthy");

    public static HealthStatusResponse Readiness(SaludResponse salud) =>
        new(salud.Estado == EstadoSalud.NoSano ? "not_ready" : "ready");

    public static HealthStatusResponse Functional(bool contextIsValid, bool insertSucceeded) =>
        new(contextIsValid && insertSucceeded ? "functional" : "not_functional");
}
