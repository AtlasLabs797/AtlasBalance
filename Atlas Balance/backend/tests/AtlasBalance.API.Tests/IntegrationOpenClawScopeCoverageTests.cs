using System.Reflection;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.07: guardarrail del contrato de scopes de OpenClaw.
//
// El middleware de integracion es deny-by-default: para dejar pasar una peticion
// exige que el token tenga en su lista el scope que `ResolveEndpointScope`
// deriva del PRIMER SEGMENTO de la ruta. Los scopes que un token puede recibir
// los valida `IntegracionesController.FindUnknownScope` contra
// `KnownOpenClawScopes`.
//
// Son dos listas mantenidas a mano en archivos distintos, y nada las ataba: si
// un endpoint nuevo estrena un segmento que no esta en `KnownOpenClawScopes`,
// ningun token puede recibir ese scope y el endpoint responde 403 para siempre.
// Fallo silencioso: no rompe ningun test, la ruta existe y el controller esta
// escrito, simplemente es inalcanzable. Le paso exactamente eso a
// `resolver-nombres` hasta V-02.07.
public sealed class IntegrationOpenClawScopeCoverageTests
{
    [Fact]
    public void Todo_Endpoint_OpenClaw_Debe_Tener_Un_Scope_Concedible()
    {
        var conocidos = GetKnownOpenClawScopes();

        var huerfanos = GetEndpointScopes()
            .Where(par => !conocidos.Contains(par.Scope, StringComparer.OrdinalIgnoreCase))
            .Select(par => $"{par.Accion} -> '{par.Scope}'")
            .ToList();

        huerfanos.Should().BeEmpty(
            "un endpoint cuyo scope no esta en KnownOpenClawScopes es inalcanzable: ningun " +
            "token puede recibir ese scope, asi que IntegrationAuthMiddleware responde 403 siempre");
    }

    [Fact]
    public void Los_Scopes_Por_Defecto_Deben_Ser_Un_Subconjunto_De_Los_Validos()
    {
        var conocidos = GetKnownOpenClawScopes();
        var porDefecto = GetScopeArray("DefaultOpenClawScopes");

        porDefecto.Should().BeSubsetOf(conocidos,
            "conceder por omision un scope que la validacion rechaza dejaria tokens en un " +
            "estado que el admin no puede reproducir ni editar desde la API");
    }

    [Fact]
    public void Resolver_Nombres_No_Debe_Concederse_Por_Defecto()
    {
        // Deshace la pseudonimizacion de nombres (re-identificacion). Es valido,
        // pero solo si el admin lo marca a proposito: omitir el campo `scopes`
        // no puede regalar capacidad de re-identificar.
        GetScopeArray("DefaultOpenClawScopes")
            .Should().NotContain(
                "resolver-nombres",
                "omitir el campo `scopes` no puede regalar capacidad de re-identificar nombres");
    }

    private static IReadOnlyList<string> GetKnownOpenClawScopes() => GetScopeArray("KnownOpenClawScopes");

    private static IReadOnlyList<string> GetScopeArray(string fieldName)
    {
        var field = typeof(IntegracionesController)
            .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull($"IntegracionesController debe declarar {fieldName}");

        var valor = field!.GetValue(null) as string[];
        valor.Should().NotBeNull($"{fieldName} debe ser un string[]");

        return valor!;
    }

    // Replica de IntegrationAuthMiddleware.ResolveEndpointScope: primer segmento
    // de la ruta, en minusculas, con el alias grafica-evolucion -> evolucion.
    private static IEnumerable<(string Accion, string Scope)> GetEndpointScopes()
    {
        var acciones = typeof(IntegrationOpenClawController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);

        foreach (var accion in acciones)
        {
            foreach (var atributo in accion.GetCustomAttributes<HttpMethodAttribute>())
            {
                var plantilla = atributo.Template;
                if (string.IsNullOrWhiteSpace(plantilla))
                {
                    continue;
                }

                var primerSegmento = plantilla
                    .Trim('/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault();

                if (string.IsNullOrWhiteSpace(primerSegmento))
                {
                    continue;
                }

                var normalizado = primerSegmento.ToLowerInvariant();
                yield return (
                    accion.Name,
                    normalizado == "grafica-evolucion" ? "evolucion" : normalizado);
            }
        }
    }
}
