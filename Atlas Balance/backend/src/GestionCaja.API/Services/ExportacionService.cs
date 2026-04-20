using System.Text;
using ClosedXML.Excel;
using GestionCaja.API.Constants;
using GestionCaja.API.Data;
using GestionCaja.API.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCaja.API.Services;

public interface IExportacionService
{
    Task<Exportacion> ExportarCuentaAsync(Guid cuentaId, TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken);
    Task<int> ExportarMensualAsync(CancellationToken cancellationToken);
}

public sealed class ExportacionService : IExportacionService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditService _auditService;

    public ExportacionService(AppDbContext dbContext, IAuditService auditService)
    {
        _dbContext = dbContext;
        _auditService = auditService;
    }

    public async Task<Exportacion> ExportarCuentaAsync(Guid cuentaId, TipoProceso tipo, Guid? iniciadoPorId, CancellationToken cancellationToken)
    {
        var cuenta = await _dbContext.Cuentas
            .Include(c => c.Titular)
            .FirstOrDefaultAsync(c => c.Id == cuentaId && c.Activa, cancellationToken);
        if (cuenta is null)
        {
            throw new InvalidOperationException("Cuenta no encontrada o inactiva");
        }

        var rawExportDirectory = await GetConfigValueAsync("export_path", @"C:\atlas-balance\exports", cancellationToken);
        var exportDirectory = ResolveSafeDirectory(rawExportDirectory, "export_path");
        Directory.CreateDirectory(exportDirectory);

        var titularSegment = Sanitize(cuenta.Titular?.Nombre ?? "Titular");
        var cuentaSegment = Sanitize(cuenta.Nombre);
        var monthTag = DateTime.UtcNow.ToString("yyyy.MM");
        var timestampTag = DateTime.UtcNow.ToString("yyyyMMdd_HHmmssfff");
        var typeTag = tipo == TipoProceso.AUTO ? "auto" : "manual";
        var fileName = $"{titularSegment}_{cuentaSegment}_{monthTag}_{typeTag}_{timestampTag}.xlsx";
        var filePath = Path.Combine(exportDirectory, fileName);

        var exportacion = new Exportacion
        {
            Id = Guid.NewGuid(),
            CuentaId = cuentaId,
            FechaExportacion = DateTime.UtcNow,
            RutaArchivo = filePath,
            Estado = EstadoProceso.PENDING,
            Tipo = tipo,
            IniciadoPorId = iniciadoPorId
        };
        _dbContext.Exportaciones.Add(exportacion);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var extractos = await _dbContext.Extractos
                .Where(e => e.CuentaId == cuentaId)
                .OrderBy(e => e.Fecha)
                .ThenBy(e => e.FilaNumero)
                .ToListAsync(cancellationToken);
            var extractoIds = extractos.Select(e => e.Id).ToList();

            var extraRows = await _dbContext.ExtractosColumnasExtra
                .Where(x => extractoIds.Contains(x.ExtractoId))
                .ToListAsync(cancellationToken);
            var extrasByExtracto = extraRows
                .GroupBy(x => x.ExtractoId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var extraColumns = extrasByExtracto.Values
                .SelectMany(v => v)
                .Select(v => v.NombreColumna)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v)
                .ToList();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Extractos");

            var headers = new List<string>
            {
                "N° Fila",
                "Fecha",
                "Concepto",
                "Monto",
                "Saldo",
                "Check",
                "Flag",
                "Nota Flag"
            };
            headers.AddRange(extraColumns);

            for (var i = 0; i < headers.Count; i++)
            {
                worksheet.Cell(1, i + 1).Value = SafeCell(headers[i]);
                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
            }

            var row = 2;
            foreach (var extracto in extractos)
            {
                worksheet.Cell(row, 1).Value = extracto.FilaNumero;
                worksheet.Cell(row, 2).Value = extracto.Fecha.ToDateTime(TimeOnly.MinValue);
                worksheet.Cell(row, 2).Style.DateFormat.Format = "yyyy-MM-dd";
                worksheet.Cell(row, 3).Value = SafeCell(extracto.Concepto);
                worksheet.Cell(row, 4).Value = extracto.Monto;
                worksheet.Cell(row, 5).Value = extracto.Saldo;
                worksheet.Cell(row, 6).Value = extracto.Checked ? "Sí" : "No";
                worksheet.Cell(row, 7).Value = extracto.Flagged ? "Sí" : "No";
                worksheet.Cell(row, 8).Value = SafeCell(extracto.FlaggedNota);

                if (extrasByExtracto.TryGetValue(extracto.Id, out var extras))
                {
                    for (var i = 0; i < extraColumns.Count; i++)
                    {
                        var val = extras.FirstOrDefault(x =>
                            string.Equals(x.NombreColumna, extraColumns[i], StringComparison.OrdinalIgnoreCase))?.Valor;
                        worksheet.Cell(row, headers.IndexOf(extraColumns[i]) + 1).Value = SafeCell(val);
                    }
                }

                row++;
            }

            worksheet.Columns().AdjustToContents();
            workbook.SaveAs(filePath);

            var fileInfo = new FileInfo(filePath);
            exportacion.Estado = EstadoProceso.SUCCESS;
            exportacion.TamanioBytes = fileInfo.Exists ? fileInfo.Length : null;
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _auditService.LogAsync(
                iniciadoPorId,
                AuditActions.ExportacionGenerada,
                "EXPORTACIONES",
                exportacion.Id,
                ipAddress: null,
                detallesJson: System.Text.Json.JsonSerializer.Serialize(new
                {
                    exportacion.CuentaId,
                    exportacion.RutaArchivo,
                    exportacion.TamanioBytes,
                    exportacion.Tipo
                }),
                cancellationToken: cancellationToken);

            _dbContext.NotificacionesAdmin.Add(new NotificacionAdmin
            {
                Id = Guid.NewGuid(),
                Tipo = "EXPORTACION",
                Mensaje = $"Exportación completada: {Path.GetFileName(filePath)}",
                Leida = false,
                Fecha = DateTime.UtcNow,
                DetallesJson = System.Text.Json.JsonSerializer.Serialize(new { exportacion.Id, exportacion.CuentaId })
            });
            await _dbContext.SaveChangesAsync(cancellationToken);

            return exportacion;
        }
        catch (Exception ex)
        {
            exportacion.Estado = EstadoProceso.FAILED;
            exportacion.TamanioBytes = null;
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException($"No se pudo generar la exportación: {ex.Message}", ex);
        }
    }

    public async Task<int> ExportarMensualAsync(CancellationToken cancellationToken)
    {
        var cuentas = await _dbContext.Cuentas
            .Where(c => c.Activa)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var success = 0;
        foreach (var cuentaId in cuentas)
        {
            try
            {
                await ExportarCuentaAsync(cuentaId, TipoProceso.AUTO, null, cancellationToken);
                success++;
            }
            catch
            {
                // Intentionally continue to avoid aborting whole monthly batch.
            }
        }

        return success;
    }

    private async Task<string> GetConfigValueAsync(string key, string fallback, CancellationToken cancellationToken)
    {
        return await _dbContext.Configuraciones
            .Where(c => c.Clave == key)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(cancellationToken) ?? fallback;
    }

    private static string SafeCell(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var first = input[0];
        return first is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + input : input;
    }

    private static string Sanitize(string input)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            sb.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        var sanitized = sb.ToString().Trim('_', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "item" : sanitized;
    }

    private static string ResolveSafeDirectory(string rawPath, string configKey)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new InvalidOperationException($"Configuración '{configKey}' vacía.");
        }

        var trimmed = rawPath.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Configuración '{configKey}' contiene segmentos de traversal.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"Configuración '{configKey}' no es una ruta válida.", ex);
        }

        if (!Path.IsPathRooted(fullPath))
        {
            throw new InvalidOperationException($"Configuración '{configKey}' debe ser una ruta absoluta.");
        }

        return fullPath;
    }
}
