using System.Reflection;
using System.Text.Json;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class HealthEndpointSecurityTests
{
    [Fact]
    public void Public_Health_Probes_Should_Expose_Only_Status()
    {
        var responses = new object[]
        {
            HealthProbeResponses.Liveness(),
            HealthProbeResponses.Readiness(new SaludResponse
            {
                Estado = EstadoSalud.NoSano,
                Comprobaciones =
                [
                    new ComprobacionSalud
                    {
                        Nombre = "base_datos",
                        Detalle = "detalle interno",
                        Valor = 42
                    }
                ],
                PeticionesUltimos5Min = 99,
                LatenciaP95Ms = 123
            }),
            HealthProbeResponses.Functional(false, false)
        };

        foreach (var response in responses)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
            document.RootElement.EnumerateObject().Select(property => property.Name)
                .Should().Equal("Status");
            document.RootElement.GetProperty("Status").GetString()
                .Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Detailed_System_Health_Should_Require_Admin_Role()
    {
        var authorize = typeof(SistemaController).GetCustomAttribute<AuthorizeAttribute>();

        authorize.Should().NotBeNull();
        authorize!.Roles.Should().Be("ADMIN");
        typeof(SistemaController).GetMethod(nameof(SistemaController.Salud))
            .Should().NotBeNull("la salud detallada debe permanecer en la superficie administrativa");
    }
}
