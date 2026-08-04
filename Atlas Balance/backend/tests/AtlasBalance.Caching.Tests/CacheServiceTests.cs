using AtlasBalance.API.Caching;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AtlasBalance.Caching.Tests;

public class CacheServiceTests
{
    private static CacheService BuildService(out IMemoryCache memoryCache)
    {
        memoryCache = new MemoryCache(new MemoryCacheOptions());
        return new CacheService(memoryCache, NullLogger<CacheService>.Instance);
    }

    [Fact]
    public async Task GetOrLoadAsync_Should_Return_Cached_Value_On_Second_Call()
    {
        var cache = BuildService(out _);
        var ns = new CacheNamespace("ns-hit");
        var calls = 0;

        var first = await cache.GetOrLoadAsync(ns, "k", _ =>
        {
            calls++;
            return Task.FromResult("value");
        }, TimeSpan.FromMinutes(1), CancellationToken.None);

        var second = await cache.GetOrLoadAsync(ns, "k", _ =>
        {
            calls++;
            return Task.FromResult("other");
        }, TimeSpan.FromMinutes(1), CancellationToken.None);

        first.Should().Be("value");
        second.Should().Be("value");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task GetOrLoadAsync_Should_Coalesce_Concurrent_Loads_Into_Single_Call()
    {
        var cache = BuildService(out _);
        var ns = new CacheNamespace("ns-singleflight");
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<int> SlowLoader(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            await gate.Task;
            return 42;
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => cache.GetOrLoadAsync(ns, "k", SlowLoader, TimeSpan.FromMinutes(1), CancellationToken.None))
            .ToArray();

        await Task.Delay(50);
        gate.SetResult();

        var results = await Task.WhenAll(tasks);
        results.Should().AllBeEquivalentTo(42);
        calls.Should().Be(1);

        var snapshot = cache.GetMetricsSnapshot(ns.Name);
        // Una sola carga efectiva (single-flight) y los 20 lectores observando
        // un valor consistente. Los hits se cuentan en los 19 que llegan al
        // try dentro del lock una vez publicada la entrada; SingleFlightWaits
        // cuenta las veces que un lector entró al lock y tuvo que cargar.
        snapshot.Loads.Should().Be(1);
        snapshot.Hits.Should().BeGreaterOrEqualTo(19);
    }

    [Fact]
    public async Task Invalidate_Should_Bump_Generation_And_Force_New_Load()
    {
        var cache = BuildService(out _);
        var ns = new CacheNamespace("ns-invalidate");
        var counter = 0;

        var first = await cache.GetOrLoadAsync(ns, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);
        var second = await cache.GetOrLoadAsync(ns, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(1);

        cache.Invalidate(ns);

        var third = await cache.GetOrLoadAsync(ns, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);
        third.Should().Be(2);
    }

    [Fact]
    public async Task Invalidate_Should_Be_Isolated_Between_Namespaces()
    {
        var cache = BuildService(out _);
        var a = new CacheNamespace("ns-a");
        var b = new CacheNamespace("ns-b");
        var counter = 0;

        var valueA = await cache.GetOrLoadAsync(a, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);
        var valueB = await cache.GetOrLoadAsync(b, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);

        valueA.Should().Be(1);
        valueB.Should().Be(2);

        cache.Invalidate(a);

        var valueAAfter = await cache.GetOrLoadAsync(a, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);
        var valueBAfter = await cache.GetOrLoadAsync(b, "k", _ => Task.FromResult(++counter), TimeSpan.FromMinutes(1), CancellationToken.None);

        valueAAfter.Should().Be(3);
        valueBAfter.Should().Be(2);
    }

    [Fact]
    public async Task Concurrent_Invalidate_During_Load_Should_Not_Repopulate_Stale_Data()
    {
        var cache = BuildService(out _);
        var ns = new CacheNamespace("ns-race");
        var loaderStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loaderCanFinish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task<string> Loader(CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            loaderStarted.TrySetResult();
            await loaderCanFinish.Task;
            return "stale";
        }

        var loadTask = cache.GetOrLoadAsync(ns, "k", Loader, TimeSpan.FromMinutes(1), CancellationToken.None);
        await loaderStarted.Task;

        // Invalidate mientras la carga está en vuelo. La siguiente lectura no debe
        // ver el valor stale cargado antes de la invalidación.
        cache.Invalidate(ns);

        loaderCanFinish.SetResult();
        var loadResult = await loadTask;
        loadResult.Should().Be("stale");

        var after = await cache.GetOrLoadAsync(ns, "k", _ => Task.FromResult("fresh"), TimeSpan.FromMinutes(1), CancellationToken.None);
        after.Should().Be("fresh");

        // La clave "stale" queda en memoria bajo la generación anterior pero el
        // GetOrLoadAsync construye una clave nueva con la generación bumped.
        // No debe devolver el stale. Lo anterior lo confirma.
    }

    [Fact]
    public async Task GetOrLoadAsync_Should_Propagate_Cancellation()
    {
        var cache = BuildService(out _);
        var ns = new CacheNamespace("ns-cancel");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await cache.GetOrLoadAsync(
            ns,
            "k",
            async ct =>
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                return 1;
            },
            TimeSpan.FromMinutes(1),
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
