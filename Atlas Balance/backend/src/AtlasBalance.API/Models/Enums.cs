namespace AtlasBalance.API.Models;

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
    EMPRESA = 0,
    PARTICULAR = 1,
    AUTONOMO = 2
}

public enum TipoCuenta
{
    NORMAL = 0,
    EFECTIVO = 1,
    PLAZO_FIJO = 2
}

public enum EstadoPlazoFijo
{
    ACTIVO = 0,
    PROXIMO_VENCER = 1,
    VENCIDO = 2,
    RENOVADO = 3,
    CANCELADO = 4
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
