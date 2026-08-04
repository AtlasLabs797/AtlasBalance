using System.Reflection;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.07: guardarrail de IDOR. `AddAuthorization()` en Program.cs no define
// FallbackPolicy, asi que una accion sin atributo explicito queda ANONIMA, no
// denegada. El vector real de IDOR en este proyecto no es un endpoint mal
// escrito sino un controller nuevo al que se le olvida el [Authorize]; con la
// configuracion actual ese olvido no falla en ningun sitio, simplemente publica
// el endpoint. Estos tests convierten ese olvido en un fallo de build.
public sealed class ControllerAuthorizationCoverageTests
{
    // Unica superficie deliberadamente anonima que NO lleva atributo de
    // autorizacion: la protege IntegrationAuthMiddleware por Bearer token propio
    // (deny-by-default, ver IntegrationAuthMiddleware.cs:115 y TokenAllowsEndpoint).
    // Si se anade otro controller aqui, hay que justificar quien lo protege.
    private static readonly HashSet<string> ControllersProtegidosPorMiddleware =
    [
        nameof(IntegrationOpenClawController)
    ];

    [Fact]
    public void Toda_Accion_De_Controller_Debe_Declarar_Autorizacion_Explicita()
    {
        var sinAutorizacion = new List<string>();

        foreach (var controller in GetControllerTypes())
        {
            if (ControllersProtegidosPorMiddleware.Contains(controller.Name))
            {
                continue;
            }

            var claseDeclara = DeclaraAutorizacion(controller);

            foreach (var action in GetActionMethods(controller))
            {
                if (claseDeclara || DeclaraAutorizacion(action))
                {
                    continue;
                }

                sinAutorizacion.Add($"{controller.Name}.{action.Name}");
            }
        }

        sinAutorizacion.Should().BeEmpty(
            "toda accion debe llevar [Authorize] o [AllowAnonymous] explicito: sin FallbackPolicy " +
            "una accion sin atributo queda publica y expone sus recursos por id (IDOR)");
    }

    [Fact]
    public void Todo_Controller_Debe_Declarar_Autorizacion_A_Nivel_De_Clase_O_Estar_Justificado()
    {
        var sinAtributoDeClase = GetControllerTypes()
            .Where(c => !DeclaraAutorizacion(c))
            .Select(c => c.Name)
            .Where(nombre => !ControllersProtegidosPorMiddleware.Contains(nombre))
            .ToList();

        // AuthController es el caso legitimo: mezcla endpoints anonimos (login,
        // refresh, mfa/verify, logout) con endpoints autenticados, asi que no
        // puede declarar el atributo en la clase y lo declara accion por accion.
        // El test anterior ya garantiza que no se le escapa ninguna.
        sinAtributoDeClase.Should().BeEquivalentTo(
            [nameof(AuthController)],
            "un controller sin atributo de clase depende de que cada accion se acuerde del suyo");
    }

    [Fact]
    public void Ningun_Endpoint_Anonimo_Debe_Aceptar_Un_Id_De_Recurso_En_La_Ruta()
    {
        var anonimosConId = new List<string>();

        foreach (var controller in GetControllerTypes())
        {
            if (ControllersProtegidosPorMiddleware.Contains(controller.Name))
            {
                continue;
            }

            var claseAnonima = controller.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

            foreach (var action in GetActionMethods(controller))
            {
                var esAnonima = action.GetCustomAttribute<AllowAnonymousAttribute>() is not null ||
                                (claseAnonima && action.GetCustomAttribute<AuthorizeAttribute>() is null);
                if (!esAnonima)
                {
                    continue;
                }

                var plantillas = action.GetCustomAttributes<HttpMethodAttribute>()
                    .Select(a => a.Template)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (plantillas.Any(t => t!.Contains('{')))
                {
                    anonimosConId.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        anonimosConId.Should().BeEmpty(
            "un endpoint anonimo con un id en la ruta es IDOR directo: cualquiera puede " +
            "enumerar el recurso sin credenciales");
    }

    private static IEnumerable<Type> GetControllerTypes()
    {
        return typeof(AuthController).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t))
            .OrderBy(t => t.Name);
    }

    private static IEnumerable<MethodInfo> GetActionMethods(Type controller)
    {
        return controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>().Any());
    }

    private static bool DeclaraAutorizacion(MemberInfo member)
    {
        return member.GetCustomAttribute<AuthorizeAttribute>() is not null ||
               member.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
    }
}
