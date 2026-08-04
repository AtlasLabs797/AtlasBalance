using AtlasBalance.API.Services;
using AtlasBalance.API.DTOs;
using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Xunit;

namespace AtlasBalance.API.Tests;

public sealed class GoogleDriveImportLimitTests
{
    [Theory]
    [InlineData("client-id")]
    [InlineData("client-secret")]
    [InlineData("folder-id")]
    public void UpdateBackupConfigRequest_Should_Reject_Overlong_Google_Fields(string field)
    {
        var request = new UpdateBackupConfigRequest();
        var oversized = new string('a', field == "client-secret" ? 2049 : field == "client-id" ? 513 : 257);
        switch (field)
        {
            case "client-id": request.GoogleDriveClientId = oversized; break;
            case "client-secret": request.GoogleDriveClientSecret = oversized; break;
            case "folder-id": request.GoogleDriveFolderId = oversized; break;
        }

        var valid = Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true);

        valid.Should().BeFalse();
    }

    [Fact]
    public void GoogleIdentifierAllowlist_Should_Reject_FolderId_Outside_Google_Allowlist()
    {
        var valid = GoogleDriveBackupService.IsSafeGoogleIdentifier("invalid/slash");

        valid.Should().BeFalse();
    }

    [Fact]
    public void ValidateCloudImportSize_Should_Reject_Metadata_Above_Limit()
    {
        var act = () => GoogleDriveBackupService.ValidateCloudImportSize(1025, 1024, "metadata");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task CopyToFileWithLimitAsync_Should_Reject_Unknown_Length_Stream_And_Delete_Partial_File()
    {
        var workDir = Path.Combine(Path.GetTempPath(), "atlas-drive-import-limit-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var destinationPath = Path.Combine(workDir, "download.dump.enc");

        try
        {
            await using var source = new MemoryStream(new byte[1025]);
            var act = () => GoogleDriveBackupService.CopyToFileWithLimitAsync(source, destinationPath, 1024, CancellationToken.None);

            await act.Should().ThrowAsync<InvalidOperationException>();
            File.Exists(destinationPath).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(workDir))
            {
                Directory.Delete(workDir, recursive: true);
            }
        }
    }
}
