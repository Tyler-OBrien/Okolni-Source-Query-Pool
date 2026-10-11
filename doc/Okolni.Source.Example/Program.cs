using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Okolni.Source.Query;
using Okolni.Source.Query.Pool.Common;
using Okolni.Source.Query.Source;

namespace Okolni.Source.Example;

public class Program
{
    private static async Task Main(string[] args)
    {
        string Host = "168.100.162.254";
        int Port = 28015;

        try
        {
            using IQueryConnection conn = new QueryConnection();
            conn.Host = Host;
            conn.Port = Port;

            conn.Setup();

            var info = await conn.GetInfoAsync();
            Console.WriteLine($"Server info: {info}");
            var players = await conn.GetPlayersAsync();
            Console.WriteLine($"Current players: {string.Join("; ", players.Players.Select(p => p.Name))}");
            var rules = await conn.GetRulesAsync();
            Console.WriteLine($"Rules: {string.Join("; ", rules.Rules.Select(r => $"{r.Key}: {r.Value}"))}");
        }
        catch (Exception ex)
        {

        }

        var lef2t = ArrayPoolInterface.NotReturnedArrays;



        using IQueryConnectionPool connPool = new QueryConnectionPool(delayInit: true);
        connPool.Message += msg =>
        {
            Console.WriteLine("Pool Responded with Message:" + msg);
        };
        connPool.Error += exception => throw exception;
        connPool.ReceiveTimeout = 5000;
        connPool.SendTimeout = 5000;
        connPool.Setup();
        var serverEndpoint1 = new IPEndPoint(IPAddress.Parse(Host), Port);
        var serverEndpoint2 = new IPEndPoint(IPAddress.Parse("104.143.3.125"), 28010);
        var serverEndpoint3 = new IPEndPoint(IPAddress.Parse("64.40.8.96"), 28015);
        var serverEndpoint4 = new IPEndPoint(IPAddress.Parse("222.124.139.169"), 27016);
        var serverEndpoint5 = new IPEndPoint(IPAddress.Parse("5.196.116.12"), 27015);


        try
        {
            var infoTask1 = connPool.GetPlayersAsync(serverEndpoint1);
            var infoTask2 = connPool.GetPlayersAsync(serverEndpoint2);
            var infoTask3 = connPool.GetPlayersAsync(serverEndpoint3);
            var infoTask4 = connPool.GetPlayersAsync(serverEndpoint4);
            var infoTask5 = connPool.GetPlayersAsync(serverEndpoint5);
            await Task.WhenAll(infoTask1, infoTask2, infoTask3, infoTask4, infoTask5);
            var (info1, info2, info3, info4, info5) = (await infoTask1, await infoTask2, await infoTask3,
                await infoTask4, await infoTask5);

            Console.WriteLine($"Server info: {info1}");
            Console.WriteLine($"Server info: {info2}");
            Console.WriteLine($"Server info: {info3}");
            Console.WriteLine($"Server info: {info4}");
            Console.WriteLine($"Server info: {info5}");
        }
        catch (Exception ex)
        {

        }

        var left = ArrayPoolInterface.NotReturnedArrays;
        Console.WriteLine($"Pool Info: Running: {connPool.Running} - Waiting: {connPool.WaitingForResponse} ");

        // If not using `using` statement
        await connPool.DisposeAsync();
        var lastLeft = ArrayPoolInterface.NotReturnedArrays;

    }
}