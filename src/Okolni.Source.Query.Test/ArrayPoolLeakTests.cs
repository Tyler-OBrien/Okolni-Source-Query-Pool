using Okolni.Source.Common;
using Okolni.Source.Query.Pool.Common;
using Okolni.Source.Query.Test.Fakes;

namespace Okolni.Source.Query.Test;

[CollectionDefinition(nameof(ArrayPoolLeakTests), DisableParallelization = true)]
public class ArrayPoolLeakCollection;

/// <summary>
///     <see cref="ArrayPoolInterface.NotReturnedArrays" /> is a process-wide counter, so these run on their own after the
///     parallel tests.
/// </summary>
[Collection(nameof(ArrayPoolLeakTests))]
public class ArrayPoolLeakTests
{
    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task SuccessfulQueries_ReturnAllRentedArrays(string kind)
    {
        var before = ArrayPoolInterface.NotReturnedArrays;
        await using (var server = new FakeA2SServer { InfoRequiresChallenge = true, MultiPacketSize = 200 })
        await using (var client = QueryClient.Create(kind))
        {
            await client.GetInfoAsync(server.EndPoint);
            await client.GetPlayersAsync(server.EndPoint);
            await client.GetRulesAsync(server.EndPoint);
        }

        await AssertReturnsTo(before);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task FailedQueries_ReturnAllRentedArrays(string kind)
    {
        var before = ArrayPoolInterface.NotReturnedArrays;
        await using (var silent = new FakeA2SServer { Silent = true })
        await using (var wrongHeader = new FakeA2SServer { WrongHeaderResponses = int.MaxValue })
        await using (var client = QueryClient.Create(kind))
        {
            await Assert.ThrowsAsync<SourceQueryException>(() => client.GetInfoAsync(silent.EndPoint, 2));
            await Assert.ThrowsAsync<SourceQueryException>(() => client.GetRulesAsync(wrongHeader.EndPoint, 2));
        }

        await AssertReturnsTo(before);
    }

    [Theory]
    [MemberData(nameof(QueryClient.Kinds), MemberType = typeof(QueryClient))]
    public async Task IncompleteMultiPacketResponse_ReturnsAllRentedArrays(string kind)
    {
        var before = ArrayPoolInterface.NotReturnedArrays;
        await using (var server = new FakeA2SServer
               {
                   Rules = Enumerable.Range(0, 50).ToDictionary(i => $"rule{i}", i => "value"),
                   MultiPacketSize = 100, DropFragmentsAfterFirst = true
               })
        await using (var client = QueryClient.Create(kind))
        {
            await Assert.ThrowsAsync<SourceQueryException>(() => client.GetRulesAsync(server.EndPoint, 2));
        }

        await AssertReturnsTo(before);
    }

    private static async Task AssertReturnsTo(int expected)
    {
        // The pool's background worker returns its last arrays as it shuts down
        try
        {
            await QueryConnectionPoolTests.WaitUntil(() => ArrayPoolInterface.NotReturnedArrays == expected);
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(expected, ArrayPoolInterface.NotReturnedArrays);
    }
}
