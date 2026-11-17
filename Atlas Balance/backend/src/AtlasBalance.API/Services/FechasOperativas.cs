namespace AtlasBalance.API.Services;

// B-16: "hoy" no puede ser el dia UTC. Entre las 00:00 y las 01:00 (invierno)
// o las 02:00 (verano) de la zona operativa de la aplicacion, el dia UTC es
// el anterior: un cierre de mes, un corte de gasto o un "que he gastado hoy"
// razonan sobre el dia equivocado.
//
// La zona operativa es Europe/Madrid (la aplicacion es on-premise en Espana)
// con caida a la zona local del servidor si el identificador IANA no esta
// disponible en el sistema.
public static class FechasOperativas
{
    public const string ZonaHorariaId = "Europe/Madrid";

    private static readonly TimeZoneInfo Zona = ResolverZona();

    public static DateTime AhoraLocal() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zona);

    public static DateOnly HoyLocal() => DateOnly.FromDateTime(AhoraLocal());

    public static DateOnly InicioDeMes(DateOnly dia) => new(dia.Year, dia.Month, 1);

    private static TimeZoneInfo ResolverZona()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ZonaHorariaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
