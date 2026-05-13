using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AtlasBalance.API.Tests;

public class NotificacionesAdminControllerTests
{
    private static AppDbContext BuildDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Resumen_And_MarcarLeidas_Should_Respect_Notification_Type()
    {
        await using var db = BuildDbContext();
        db.NotificacionesAdmin.AddRange(
            new NotificacionAdmin
            {
                Id = Guid.NewGuid(),
                Tipo = "EXPORTACION",
                Mensaje = "Exportacion lista",
                Leida = false
            },
            new NotificacionAdmin
            {
                Id = Guid.NewGuid(),
                Tipo = "EXPORTACION",
                Mensaje = "Otra exportacion lista",
                Leida = false
            },
            new NotificacionAdmin
            {
                Id = Guid.NewGuid(),
                Tipo = "ALERTA",
                Mensaje = "Alerta activa",
                Leida = false
            });
        await db.SaveChangesAsync();

        var controller = new NotificacionesAdminController(db);

        var resumenBefore = await controller.Resumen(CancellationToken.None);
        var beforePayload = resumenBefore.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<NotificacionesAdminResumenResponse>().Subject;

        beforePayload.ExportacionesPendientes.Should().Be(2);
        beforePayload.TotalPendientes.Should().Be(3);

        var markResult = await controller.MarcarLeidas(
            new MarcarNotificacionesLeidasRequest { Tipo = "EXPORTACION" },
            CancellationToken.None);

        markResult.Should().BeOfType<OkObjectResult>();

        var resumenAfter = await controller.Resumen(CancellationToken.None);
        var afterPayload = resumenAfter.Should().BeOfType<OkObjectResult>().Subject.Value
            .Should().BeOfType<NotificacionesAdminResumenResponse>().Subject;

        afterPayload.ExportacionesPendientes.Should().Be(0);
        afterPayload.TotalPendientes.Should().Be(1);
    }
}
