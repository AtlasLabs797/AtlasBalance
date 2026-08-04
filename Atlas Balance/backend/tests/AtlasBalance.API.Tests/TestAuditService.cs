using AtlasBalance.API.Data;
using AtlasBalance.API.Logging;
using AtlasBalance.API.Models;
using AtlasBalance.API.Services;
using Microsoft.AspNetCore.Http;

namespace AtlasBalance.API.Tests;

/// <summary>
/// V-02.07: AuditService paso a depender de IHttpContextAccessor (para el
/// contexto de peticion), IAuditSigner (firma HMAC) y ISecurityEventLog
/// (espejo fuera de la BD). Esta fabrica evita repetir ese cableado en los
/// ~170 sitios de la suite que solo quieren "un AuditService que escriba".
///
/// Sin HttpContext, las filas quedan con Origen = JOB, que es exactamente lo
/// que corresponde a un test que no simula una peticion.
/// </summary>
internal static class TestAuditService
{
    /// <summary>Clave de firma fija: los tests deben ser deterministas.</summary>
    internal const string SigningKey = "clave-de-firma-de-auditoria-para-tests-32+";

    internal static AuditSigner Signer() => new(new AuditSigningKey(SigningKey));

    internal static AuditService Create(
        AppDbContext dbContext,
        IHttpContextAccessor? httpContextAccessor = null)
        => new(
            dbContext,
            httpContextAccessor ?? new HttpContextAccessor(),
            Signer(),
            new NoOpSecurityEventLog());

    /// <summary>
    /// El espejo real escribe en el Windows Event Log. En tests se anula: no es
    /// lo que se esta probando y ensuciaria el log del sistema.
    /// </summary>
    internal sealed class NoOpSecurityEventLog : ISecurityEventLog
    {
        public void RegistrarSiEsRelevante(Auditoria auditoria) { }
    }
}
