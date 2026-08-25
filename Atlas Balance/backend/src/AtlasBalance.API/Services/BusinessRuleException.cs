namespace AtlasBalance.API.Services;

// SEC V-02.09: excepcion de regla de negocio con mensaje pensado para el
// usuario. Los controllers la devuelven tal cual (400/404 segun el caso);
// cualquier otro tipo de excepcion (InvalidOperationException tecnico, fallos
// de infraestructura, etc.) ya NO se filtra al cliente: cae en el handler
// global que responde 500 generico. Antes los controllers devolvian
// ex.Message de InvalidOperationException y cualquier mensaje tecnico futuro
// de un servicio habria acabado en la respuesta HTTP.
public sealed class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }
}
