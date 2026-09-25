using AtlasBalance.API;
using AtlasBalance.API.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class AuthorizationConfigurationTests
{
    [Fact]
    public void FallbackPolicy_Debe_Exigir_Usuario_Autenticado()
    {
        var services = new ServiceCollection();
        services.AddAuthorization(AuthorizationConfiguration.Configure);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        options.FallbackPolicy.Should().NotBeNull();
        options.FallbackPolicy!.Requirements
            .Should().ContainSingle(requirement => requirement is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void Excepciones_De_Auth_E_Integracion_Deben_Ser_Explicitas()
    {
        GetAction(nameof(AuthController.Login)).Should().BeDecoratedWith<AllowAnonymousAttribute>();
        GetAction(nameof(AuthController.RefreshToken)).Should().BeDecoratedWith<AllowAnonymousAttribute>();
        GetAction(nameof(AuthController.VerifyMfa)).Should().BeDecoratedWith<AllowAnonymousAttribute>();
        GetAction(nameof(AuthController.Logout)).Should().BeDecoratedWith<AllowAnonymousAttribute>();
        typeof(IntegrationOpenClawController)
            .Should().BeDecoratedWith<AllowAnonymousAttribute>();
    }

    [Fact]
    public void Accion_Protegida_Debe_Mantener_Authorize_Explicito()
    {
        GetAction(nameof(AuthController.Me)).Should().BeDecoratedWith<AuthorizeAttribute>();
    }

    private static System.Reflection.MethodInfo GetAction(string name) =>
        typeof(AuthController).GetMethod(name) ?? throw new InvalidOperationException($"Accion no encontrada: {name}");
}
