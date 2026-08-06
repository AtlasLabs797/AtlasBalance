using System.Text.Json;

namespace AtlasBalance.API.Services.IaPlanner;

// Una unica frontera para los campos de auditoria de IA. Los callers solo
// aportan valores estructurados; pregunta, respuesta y errores crudos no tienen
// representacion en este contrato.
public static class IaAuditEventFactory
{
    public static string Consulta(string origen, IReadOnlyDictionary<string, object?> fields) =>
        Serializar(origen, IaAuditSchema.CamposConsultaPermitidos, fields);

    public static string Bloqueada(string origen, IReadOnlyDictionary<string, object?> fields) =>
        Serializar(origen, IaAuditSchema.CamposBloqueadaPermitidos, fields);

    public static string Error(string origen, IReadOnlyDictionary<string, object?> fields) =>
        Serializar(origen, IaAuditSchema.CamposErrorPermitidos, fields);

    private static string Serializar(string origen, IReadOnlyList<string> allowed, IReadOnlyDictionary<string, object?> fields)
    {
        var payload = new SortedDictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schema_version"] = IaAuditSchema.Version,
            ["origen"] = origen
        };
        foreach (var (key, value) in fields)
        {
            if (allowed.Contains(key, StringComparer.Ordinal)) payload[key] = value;
        }
        return JsonSerializer.Serialize(payload);
    }
}
