using System.Security.Claims;
using FluentAssertions;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Data;
using AtlasBalance.API.DTOs;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class RevisionControllerTests
{
    [Fact]
    public async Task ActualizarEstado_Should_Return_Forbid_When_Extracto_Cuenta_Is_Outside_User_Scope()
    {
        var controller = BuildController(scope =>
            throw new UnauthorizedAccessException());

        var result = await controller.ActualizarEstado(
            "comision",
            Guid.NewGuid(),
            new UpdateRevisionEstadoRequest { Estado = "revisado" },
            CancellationToken.None);

        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ActualizarEstado_Should_Return_BadRequest_When_Payload_Is_Null()
    {
        var controller = BuildController(_ => Task.CompletedTask);

        var result = await controller.ActualizarEstado(
            "comision",
            Guid.NewGuid(),
            request: null!,
            CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task ActualizarEstado_Should_Return_Ok_When_Extracto_Belongs_To_User_Scope()
    {
        var capturedScope = (UserAccessScope?)null;
        var controller = BuildController(scope =>
        {
            capturedScope = scope;
            return Task.CompletedTask;
        });

        var extractoId = Guid.NewGuid();
        var result = await controller.ActualizarEstado(
            "comision",
            extractoId,
            new UpdateRevisionEstadoRequest { Estado = "revisado" },
            CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        capturedScope.Should().NotBeNull();
        capturedScope!.UserId.Should().NotBe(Guid.Empty);
    }

    private static RevisionController BuildController(Func<UserAccessScope, Task> setEstado)
    {
        var userId = Guid.NewGuid();
        var userAccessService = new StubUserAccessService(new UserAccessScope
        {
            UserId = userId,
            HasPermissions = true,
            HasGlobalAccess = false,
            TitularIds = [Guid.NewGuid()]
        });
        var revisionService = new StubRevisionService(setEstado);
        var controller = new RevisionController(revisionService, userAccessService);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, RolUsuario.GERENTE.ToString())
        ], "TestAuth");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };

        return controller;
    }

    private sealed class StubUserAccessService : IUserAccessService
    {
        private readonly UserAccessScope _scope;

        public StubUserAccessService(UserAccessScope scope)
        {
            _scope = scope;
        }

        public Task<UserAccessScope> GetScopeAsync(System.Security.Claims.ClaimsPrincipal user, CancellationToken cancellationToken)
            => Task.FromResult(_scope);

        public IQueryable<Titular> ApplyTitularScope(IQueryable<Titular> query, UserAccessScope scope) => query;
        public IQueryable<Cuenta> ApplyCuentaScope(IQueryable<Cuenta> query, UserAccessScope scope) => query;

        public Task<bool> CanAccessTitularAsync(Guid titularId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.TitularIds.Contains(titularId) || _scope.HasGlobalAccess);
        public Task<bool> CanAccessCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
        public Task<bool> CanWriteCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
        public Task<bool> CanEditCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
        public Task<bool> CanReviewCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
        public Task<bool> CanApproveImportacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
        public Task<bool> CanConciliarCuentaAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
        public Task<bool> CanCerrarConciliacionAsync(Guid cuentaId, UserAccessScope scope, CancellationToken cancellationToken)
            => Task.FromResult(_scope.HasGlobalAccess);
    }

    private sealed class StubRevisionService : IRevisionService
    {
        private readonly Func<UserAccessScope, Task> _setEstado;

        public StubRevisionService(Func<UserAccessScope, Task> setEstado)
        {
            _setEstado = setEstado;
        }

        public Task<RevisionSettingsResponse> GetSettingsAsync(CancellationToken cancellationToken)
            => Task.FromResult(new RevisionSettingsResponse());

        public Task<PaginatedResponse<RevisionComisionItemResponse>> GetComisionesAsync(UserAccessScope scope, RevisionQueryRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new PaginatedResponse<RevisionComisionItemResponse>());

        public Task<PaginatedResponse<RevisionSeguroItemResponse>> GetSegurosAsync(UserAccessScope scope, RevisionQueryRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new PaginatedResponse<RevisionSeguroItemResponse>());

        public Task SetEstadoAsync(UserAccessScope scope, Guid extractoId, string tipo, string estado, CancellationToken cancellationToken)
            => _setEstado(scope);

        public Task<VerificarDevolucionResponse> VerificarDevolucionAsync(UserAccessScope scope, Guid extractoId, CancellationToken cancellationToken)
            => Task.FromResult(new VerificarDevolucionResponse { Encontrada = true, Message = "Devolucion verificada" });
    }
}
