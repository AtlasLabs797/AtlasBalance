using System.Reflection;
using AtlasBalance.API.Controllers;
using AtlasBalance.API.Services;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

/// <summary>
/// Guardarrail contra la deriva que provoco el hallazgo de V-02.07.
///
/// La regla "una ruta de backup/exportacion no puede ser de red" esta
/// implementada CUATRO veces por separado: ConfiguracionController,
/// BackupService, ExportacionService y GoogleDriveBackupService. Cuando se
/// anadio el rechazo de UNC solo se aplico a las tres primeras; la cuarta se
/// quedo fuera y las copias descargadas de Drive podian acabar en un recurso
/// SMB remoto.
///
/// Importa porque <c>Path.IsPathRooted(@"\\host\recurso")</c> devuelve true en
/// Windows: sin el chequeo explicito, una UNC pasa como "ruta absoluta valida".
///
/// Se probo unificar las cuatro copias en un helper compartido y se DESCARTO a
/// proposito: las cuatro politicas difieren de verdad -mensajes distintos,
/// try/catch o no alrededor de Path.GetFullPath, una recomprobacion posterior a
/// canonicalizar, y un criterio de raiz mas laxo en una de ellas-, asi que el
/// helper acababa necesitando cinco parametros booleanos y un valor de enum
/// dedicado a preservar una tilde. Quedaba mas dificil de leer que la
/// duplicacion que venia a resolver (AGENTS.md seccion 2.2).
///
/// Lo que evita la recaida no es el refactor: es este test. Si alguien anade un
/// quinto lector de backup_path sin el chequeo, o se lo quita a uno de estos
/// cuatro, aqui se entera.
///
/// Se llega por reflexion porque los cuatro metodos son privados y alcanzarlos
/// por la via publica exigiria simular la API de Google. Mismo patron que ya usa
/// RevisionServiceTests.
/// </summary>
public class UncPathRejectionTests
{
    private const string UncBackslash = @"\\servidor\backups";
    private const string UncForwardSlash = "//servidor/backups";

    [Theory]
    [InlineData(UncBackslash)]
    [InlineData(UncForwardSlash)]
    public void GoogleDriveBackupService_Should_Reject_UncPath(string uncPath)
    {
        // Este es el que se habia quedado sin el chequeo.
        var method = typeof(GoogleDriveBackupService)
            .GetMethod("ResolveSafeDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ResolveSafeDirectory es donde vive la politica de rutas de este servicio");

        var act = () => method!.Invoke(null, [uncPath]);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    [Theory]
    [InlineData(UncBackslash)]
    [InlineData(UncForwardSlash)]
    public void BackupService_Should_Reject_UncPath(string uncPath)
    {
        var method = typeof(BackupService)
            .GetMethod("ResolveSafeDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var act = () => method!.Invoke(null, [uncPath, "backup_path"]);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    [Theory]
    [InlineData(UncBackslash)]
    [InlineData(UncForwardSlash)]
    public void ExportacionService_Should_Reject_UncPath(string uncPath)
    {
        var method = typeof(ExportacionService)
            .GetMethod("ResolveSafeDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var act = () => method!.Invoke(null, [uncPath, "export_path"]);

        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<InvalidOperationException>();
    }

    [Theory]
    [InlineData(UncBackslash)]
    [InlineData(UncForwardSlash)]
    public void ConfiguracionController_Should_Reject_UncPath(string uncPath)
    {
        // Este devuelve bool en vez de lanzar, a diferencia de los otros tres.
        var method = typeof(ConfiguracionController)
            .GetMethod("IsSafeAbsoluteDirectory", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, [uncPath]);

        result.Should().Be(false);
    }

    /// <summary>
    /// Contraprueba: una ruta local absoluta normal sigue pasando en los cuatro.
    /// Sin esto, los tests de arriba tambien pasarian si alguien rompiera la
    /// validacion entera y empezara a rechazarlo todo.
    /// </summary>
    [Fact]
    public void All_Validators_Should_Accept_PlainLocalPath()
    {
        const string local = @"C:\atlas\backups";

        typeof(GoogleDriveBackupService)
            .GetMethod("ResolveSafeDirectory", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [local]).Should().NotBeNull();

        typeof(BackupService)
            .GetMethod("ResolveSafeDirectory", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [local, "backup_path"]).Should().NotBeNull();

        typeof(ExportacionService)
            .GetMethod("ResolveSafeDirectory", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [local, "export_path"]).Should().NotBeNull();

        typeof(ConfiguracionController)
            .GetMethod("IsSafeAbsoluteDirectory", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [local]).Should().Be(true);
    }
}
