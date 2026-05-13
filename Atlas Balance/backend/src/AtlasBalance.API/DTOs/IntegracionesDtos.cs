namespace AtlasBalance.API.DTOs;

public sealed class IntegrationPermissionItemResponse
{
    public Guid Id { get; set; }
    public Guid? TitularId { get; set; }
    public Guid? CuentaId { get; set; }
    public string AccesoTipo { get; set; } = "lectura";
}

public sealed class IntegrationTokenListItemResponse
{
    public Guid Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Tipo { get; set; } = "openclaw";
    public string Estado { get; set; } = string.Empty;
    public bool PermisoLectura { get; set; }
    public bool PermisoEscritura { get; set; }
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaUltimaUso { get; set; }
    public DateTime? FechaRevocacion { get; set; }
    public Guid UsuarioCreadorId { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public sealed class IntegrationTokenDetailResponse
{
    public IntegrationTokenListItemResponse Token { get; set; } = new();
    public IReadOnlyList<IntegrationPermissionItemResponse> Permisos { get; set; } = [];
}

public sealed class CreateIntegrationTokenRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool PermisoLectura { get; set; } = true;
    public bool PermisoEscritura { get; set; }
    public IReadOnlyList<SaveIntegrationPermissionRequest> Permisos { get; set; } = [];
}

public sealed class SaveIntegrationTokenRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public bool PermisoLectura { get; set; } = true;
    public bool PermisoEscritura { get; set; }
    public IReadOnlyList<SaveIntegrationPermissionRequest> Permisos { get; set; } = [];
}

public sealed class SaveIntegrationPermissionRequest
{
    public Guid? TitularId { get; set; }
    public Guid? CuentaId { get; set; }
    public string AccesoTipo { get; set; } = "lectura";
}

public sealed class CreateIntegrationTokenResponse
{
    public IntegrationTokenDetailResponse Token { get; set; } = new();
    public string TokenPlano { get; set; } = string.Empty;
}

public sealed class IntegrationAuditItemResponse
{
    public Guid Id { get; set; }
    public Guid TokenId { get; set; }
    public string? TokenNombre { get; set; }
    public string Endpoint { get; set; } = string.Empty;
    public string Metodo { get; set; } = string.Empty;
    public int? CodigoRespuesta { get; set; }
    public DateTime Timestamp { get; set; }
    public int? TiempoEjecucionMs { get; set; }
    public string? IpAddress { get; set; }
}

public sealed class IntegrationApiResponse<T>
{
    public bool Exito { get; set; }
    public T? Datos { get; set; }
    public IReadOnlyList<string> Errores { get; set; } = [];
    public IReadOnlyList<string> Advertencias { get; set; } = [];
    public IntegrationApiMetadata Metadata { get; set; } = new();
}

public sealed class IntegrationApiMetadata
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string VersionApi { get; set; } = "1.0";
    public string TokenTipo { get; set; } = "openclaw";
}

public static class IntegrationApiResponses
{
    public static IntegrationApiResponse<T> Success<T>(T data, IReadOnlyList<string>? warnings = null)
    {
        return new IntegrationApiResponse<T>
        {
            Exito = true,
            Datos = data,
            Advertencias = warnings ?? [],
            Metadata = CreateMetadata()
        };
    }

    public static IntegrationApiResponse<object?> Failure(string error)
    {
        return new IntegrationApiResponse<object?>
        {
            Exito = false,
            Datos = null,
            Errores = [error],
            Metadata = CreateMetadata()
        };
    }

    private static IntegrationApiMetadata CreateMetadata() => new();
}
