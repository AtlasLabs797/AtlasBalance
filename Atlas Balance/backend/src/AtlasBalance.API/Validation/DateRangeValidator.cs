namespace AtlasBalance.API.Validation;

/// <summary>
/// Comprobacion de rangos de fecha para los filtros de listado.
///
/// V-02.07: el rango invertido solo se rechazaba en los endpoints de la
/// integracion OpenClaw. En auditoria y en el listado de extractos las fechas
/// iban directas a la query, asi que pedir "del 31 al 1" devolvia cero filas y
/// un 200. El usuario no distingue eso de "no hay datos", y en auditoria puede
/// llevar a dar por bueno que no existe rastro de algo cuando lo que esta mal es
/// el filtro.
/// </summary>
public static class DateRangeValidator
{
    /// <summary>
    /// Devuelve false si ambas fechas vienen y estan al reves. Un rango abierto
    /// por cualquiera de los dos lados es valido: significa "sin limite por ahi".
    /// </summary>
    public static bool TryValidate(DateOnly? desde, DateOnly? hasta, out string error)
    {
        if (desde.HasValue && hasta.HasValue && desde.Value > hasta.Value)
        {
            error = "La fecha de inicio no puede ser posterior a la fecha de fin.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
