using AtlasBalance.API.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AtlasBalance.API.Tests;

// -----------------------------------------------------------------------
// S-18: las respuestas de /api/* deben llevar Cache-Control: no-store.
// Los datos de la API (saldos, extractos) no pueden quedar en la cache de
// un proxy o del navegador. Los estaticos de wwwroot no se tocan: siguen
// con la politica de cache actual (el html nunca se cachea, los assets con
// hash si).
// -----------------------------------------------------------------------
public sealed class SecurityLot2Tests
{
    [Fact]
    public void ApplyCacheControl_Should_Set_NoStore_For_ApiPaths()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/usuarios";

        SecurityResponseHeaders.ApplyCacheControl(context);

        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-store");
    }

    [Fact]
    public void ApplyCacheControl_Should_Not_Set_NoStore_For_StaticFiles()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/index.html";

        SecurityResponseHeaders.ApplyCacheControl(context);

        context.Response.Headers.ContainsKey("Cache-Control").Should().BeFalse();
    }

    [Fact]
    public void ApplyCacheControl_Should_Not_Overwrite_An_Existing_Header()
    {
        // Si el controlador ya fijo su propia Cache-Control, no se pisa.
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/usuarios";
        context.Response.Headers["Cache-Control"] = "no-cache";

        SecurityResponseHeaders.ApplyCacheControl(context);

        context.Response.Headers["Cache-Control"].ToString().Should().Be("no-cache");
    }
}
