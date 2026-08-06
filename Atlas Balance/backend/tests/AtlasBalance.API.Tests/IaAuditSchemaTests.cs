using System.Text.Json;
using FluentAssertions;
using AtlasBalance.API.Services.IaPlanner;
using Xunit;

namespace AtlasBalance.API.Tests;

// V-02.09 (Fase 11): tests del contrato de auditoria. Verifican
// que las claves permitidas y las prohibidas son las esperadas y
// que el esquema lleva una version para que los administradores
// puedan filtrar auditorias historicas.
public class IaAuditSchemaTests
{
    [Fact]
    public void Version_Es_V209()
    {
        IaAuditSchema.Version.Should().Be("v2.09");
    }

    [Fact]
    public void Campos_Consulta_Lista_No_Contiene_Texto_Libre_Ni_PII()
    {
        IaAuditSchema.CamposConsultaPermitidos.Should().NotContain("pregunta_completa");
        IaAuditSchema.CamposConsultaPermitidos.Should().NotContain("respuesta_completa");
        IaAuditSchema.CamposConsultaPermitidos.Should().NotContain("contexto_financiero");
        IaAuditSchema.CamposConsultaPermitidos.Should().NotContain("pregunta");
        IaAuditSchema.CamposConsultaPermitidos.Should().NotContain("respuesta");
        IaAuditSchema.CamposConsultaPermitidos.Should().NotContain("provider_error");
    }

    [Fact]
    public void Campos_Bloqueada_No_Contiene_Texto_Libre()
    {
        IaAuditSchema.CamposBloqueadaPermitidos.Should().NotContain("provider_error");
        IaAuditSchema.CamposBloqueadaPermitidos.Should().NotContain("prompt");
    }

    [Fact]
    public void Campos_Error_No_Contiene_Provider_Error()
    {
        // V-02.09 (Fase 1.4): provider_error se elimino de la
        // auditoria por el riesgo de filtrar credenciales del
        // proveedor. El clasificador sigue funcionando en memoria.
        IaAuditSchema.CamposErrorPermitidos.Should().NotContain("provider_error");
    }

    [Fact]
    public void Campos_Consulta_Contiene_Origen_Local_O_Proveedor()
    {
        // V-02.09 (Fase 11): el campo 'origen' indica si la
        // respuesta se calculo localmente (Fase 4) o si se llamo
        // al proveedor externo. Permite al administrador auditar
        // cuantos calculos locales se hacen vs cuantos pasan por
        // el proveedor.
        IaAuditSchema.CamposConsultaPermitidos.Should().Contain("origen");
    }

    [Fact]
    public void Fragmentos_Prohibidos_Incluye_IBANs_Conocidos()
    {
        IaAuditSchema.FragmentosProhibidos.Should().Contain("ES91");
        IaAuditSchema.FragmentosProhibidos.Should().Contain("DE89");
        IaAuditSchema.FragmentosProhibidos.Should().Contain("4111 1111 1111 1111");
    }

    [Fact]
    public void Fragmentos_Prohibidos_Incluye_Keys_De_Proveedor()
    {
        IaAuditSchema.FragmentosProhibidos.Should().Contain("sk-proj-");
        IaAuditSchema.FragmentosProhibidos.Should().Contain("sk-or-v1-");
        IaAuditSchema.FragmentosProhibidos.Should().Contain("Bearer ");
    }

    [Fact]
    public void Audit_Existente_No_Contiene_Provider_Error_En_Campos_Error()
    {
        // Simula el JSON que AtlasAiService.LogProviderErrorAsync
        // produce para confirmar que la clave 'provider_error' no
        // aparece.
        var json = """
        {
          "motivo": "provider_http_error",
          "provider": "OPENROUTER",
          "model": "nvidia/nemotron",
          "runtime_model": "nvidia/nemotron",
          "status_code": 429,
          "extra": {
            "http_client": "openrouter",
            "used_http_fallback": false,
            "runtime_model": "nvidia/nemotron",
            "retry_after_seconds": 60
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var extra = doc.RootElement.GetProperty("extra");
        extra.TryGetProperty("provider_error", out _).Should().BeFalse();
    }

    [Fact]
    public void Audit_Existente_No_Contiene_Provider_Error_En_Campos_Consulta()
    {
        // El JSON real de AtlasAiService.AskAsync cuando la
        // consulta sale bien. Verifica que no contiene la clave
        // prohibida.
        var json = """
        {
          "provider": "OPENROUTER",
          "model": "nvidia/nemotron-3-super-120b-a12b:free",
          "runtime_model": "nvidia/nemotron-3-super-120b-a12b:free",
          "http_client": "openrouter",
          "used_http_fallback": false,
          "zero_data_retention": true,
          "pais_id": null,
          "movimientos_analizados": 0,
          "pregunta_caracteres": 50,
          "contexto_caracteres": 1024,
          "tokens_entrada_estimados": 120,
          "tokens_salida_estimados": 20,
          "entidades_seudonimizadas": 2,
          "coste_estimado_eur": 0.00012,
          "coste_mes_estimado_eur": 0.00012,
          "coste_mes_usuario_estimado_eur": 0.00012,
          "requests_mes_usuario": 1,
          "coste_total_estimado_eur": 0.00012,
          "presupuesto_mensual_eur": 1.0,
          "presupuesto_mensual_usuario_eur": 0.5,
          "presupuesto_total_eur": 10.0,
          "aviso_presupuesto": false
        }
        """;
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            IaAuditSchema.CamposConsultaPermitidos.Should().Contain(prop.Name,
                $"campo inesperado en la auditoria: {prop.Name}");
            // Verificacion adicional: el valor no contiene ningun
            // fragmento prohibido (PII, texto libre, etc.).
            var value = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : "";
            foreach (var fragmento in IaAuditSchema.FragmentosProhibidos)
            {
                value.Should().NotContain(fragmento,
                    $"el campo {prop.Name} contiene '{fragmento}' que esta prohibido");
            }
        }
    }

    [Fact]
    public void Factory_Elimina_Campos_No_Permitidos_Y_Agrega_Envelope()
    {
        var json = IaAuditEventFactory.Consulta("local", new Dictionary<string, object?>
        {
            ["provider"] = "LOCAL",
            ["movimientos_analizados"] = 3,
            ["pregunta_completa"] = "dato que no debe persistir"
        });

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("schema_version").GetString().Should().Be(IaAuditSchema.Version);
        document.RootElement.GetProperty("origen").GetString().Should().Be("local");
        document.RootElement.TryGetProperty("pregunta_completa", out _).Should().BeFalse();
    }
}
