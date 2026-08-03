using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Testcontainers.PostgreSql;
using Xunit;

namespace AtlasBalance.API.Tests;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private static readonly string ContainerCredential = string.Concat("test-", Guid.NewGuid().ToString("N"));

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine@sha256:4e6e670bb069649261c9c18031f0aded7bb249a5b6664ddec29c013a89310d50")
        .WithImagePullPolicy(PullPolicy.Missing)
        .WithDatabase("atlas_balance_tests")
        .WithUsername("app_user")
        .WithPassword(ContainerCredential)
        .WithCleanUp(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    // xunit v3: IAsyncLifetime hereda de IAsyncDisposable y ambos metodos
    // devuelven ValueTask, no Task.
    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
