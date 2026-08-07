namespace AtlasBalance.API.Services.IaPlanner;

// V-02.09 (Fase 11): contrato explicito de auditoria para la IA.
//
// Define QUÉ se persiste en el campo DetallesJson de AUDITORIAS
// cuando el evento es IaConsulta / IaConsultaError / IaConsultaBloqueada.
// El objetivo es que sea facil auditar: cualquier administrador con
// acceso a AUDITORIAS puede verificar de un vistazo que no hay
// datos sensibles.
//
// Los tests de IaAuditSchemaTests pinzan este contrato para que un
// cambio accidental en AtlasAiService que vuelva a meter texto
// libre o PII en la auditoria falle el test.
public static class IaAuditSchema
{
    // Version del esquema. Incrementar cuando se anade o elimina
    // un campo del JSON persistido. Los administradores que tengan
    // consultas de auditoria contra los campos pueden filtrar por
    // esta version.
    public const string Version = "v2.09";

    // Claves permitidas en el JSON de IaConsulta.
    public static readonly IReadOnlyList<string> CamposConsultaPermitidos = new[]
    {
        "schema_version",
        "usuario_id",
        "provider",
        "model",
        "runtime_model",
        "http_client",
        "used_http_fallback",
        "zero_data_retention",
        "pais_id",
        "movimientos_analizados",
        "pregunta_caracteres",
        "contexto_caracteres",
        "tokens_entrada_estimados",
        "tokens_salida_estimados",
        "entidades_seudonimizadas",
        "coste_estimado_eur",
        "coste_mes_estimado_eur",
        "coste_mes_usuario_estimado_eur",
        "requests_mes_usuario",
        "coste_total_estimado_eur",
        "presupuesto_mensual_eur",
        "presupuesto_mensual_usuario_eur",
        "presupuesto_total_eur",
        "aviso_presupuesto",
        "origen"  // 'local' (Fase 4) o 'proveedor'
    };

    // Claves permitidas en el JSON de IaConsultaBloqueada.
    public static readonly IReadOnlyList<string> CamposBloqueadaPermitidos = new[]
    {
        "schema_version",
        "origen",
        "motivo",
        "provider",
        "model",
        "runtime_model",
        "requested_model",
        "requested_thinking_mode",
        "pais_id"
    };

    // Claves permitidas en el extra de IaConsultaError.
    public static readonly IReadOnlyList<string> CamposErrorPermitidos = new[]
    {
        "schema_version",
        "origen",
        "motivo",
        "provider",
        "model",
        "runtime_model",
        "status_code",
        "http_client",
        "used_http_fallback",
        "runtime_model",
        "provider_response_error_kind",
        "finish_reason",
        "retry_after_seconds"
    };

    // Fragmentos prohibidos en cualquier campo de la auditoria.
    // Si aparecen, el sistema ha dejado escapar informacion que
    // no debe persistirse (PII, prompt completo, respuesta
    // completa, free text del proveedor).
    public static readonly IReadOnlyList<string> FragmentosProhibidos = new[]
    {
        "@",          // email
        "ES91",       // IBAN espanol canonico
        "DE89",       // IBAN aleman
        "FR14",       // IBAN frances
        "4111 1111 1111 1111", // tarjeta de prueba
        "12345678Z",  // DNI
        "X1234567A",  // NIE
        "sk-proj-",   // API key OpenAI
        "sk-or-v1-",  // API key OpenRouter
        "Bearer ",    // header de autenticacion
        "provider_error"  // texto libre del proveedor (Fase 1.4 lo elimino)
    };

    // Palabras que indican que la auditoria contiene texto libre
    // cuando no deberia. Un test debe fallar si aparecen.
    public const string FragmentoTextoLibreProhibido = "El proveedor devolvio:";
    public const string FragmentoPromptCompleto = "pregunta_completa";
    public const string FragmentoRespuestaCompleta = "respuesta_completa";
    public const string FragmentoContextoFinanciero = "contexto_financiero";
}
