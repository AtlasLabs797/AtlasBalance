namespace AtlasBalance.API.Services;

/// <summary>
/// V-02.07: seudonimos estables y opacos para los nombres de titular y cuenta
/// que se exponen a la integracion externa OpenClaw. El GUID completo de la
/// entidad ya viaja en el campo "id" de cada respuesta, asi que el seudonimo
/// no anade informacion nueva: es solo una etiqueta opaca y estable derivada
/// de ese mismo GUID, para que un consumidor que retenga los datos no sepa de
/// quien son sin llamar al endpoint de resolucion bajo demanda (que aplica el
/// scope del token). No hace falta secreto ni hashing.
/// </summary>
public static class IntegrationPseudonyms
{
    public static string ForTitular(Guid id) => "TITULAR-" + id.ToString("N").Substring(0, 8);

    public static string ForCuenta(Guid id) => "CUENTA-" + id.ToString("N").Substring(0, 8);
}
