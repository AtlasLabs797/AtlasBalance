namespace AtlasBalance.API.Data;

// V-02.08: DTO auxiliar para que EF SqlQueryRaw pueda deserializar el
// resultado de `SELECT atlas_security.context_is_valid()` en el endpoint
// `/api/health/functional`. No es una entidad de dominio; vive aparte
// para no contaminar el modelo.
internal sealed class ContextoRlsValidoDto
{
    public bool Value { get; set; }
}
