namespace GestionCaja.API.Models;

public enum RolUsuario
{
    ADMIN,
    GERENTE,
    EMPLEADO_ULTRA,
    EMPLEADO_PLUS,
    EMPLEADO
}

public enum TipoTitular
{
    EMPRESA,
    PARTICULAR
}

public enum EstadoTokenIntegracion
{
    Activo,
    Revocado
}

public enum FuenteTipoCambio
{
    API,
    MANUAL
}

public enum EstadoProceso
{
    PENDING,
    SUCCESS,
    FAILED
}

public enum TipoProceso
{
    AUTO,
    MANUAL
}
