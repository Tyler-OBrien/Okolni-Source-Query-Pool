using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Okolni.Source.Query;
using Okolni.Source.Query.Source;

namespace Okolni.Source.Example;

public class Program
{
    private static async Task Main(string[] args)
    {
        string Host = "83.137.228.99";
        int Port = 28015;
        
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

        


        using IQueryConnectionPool connPool = new QueryConnectionPool(delayInit: true);
        connPool.Message += msg =>
        {
            Console.WriteLine("Pool Responded with Message:" + msg);
        };
        connPool.Error += exception => throw exception;
        connPool.ReceiveTimeout = 500;
        connPool.SendTimeout = 500;
        connPool.Setup();
        var serverEndpoint1 = new IPEndPoint(IPAddress.Parse(Host), Port);
        var serverEndpoint2 = new IPEndPoint(IPAddress.Parse("202.165.126.235"), 2303);
        var serverEndpoint3 = new IPEndPoint(IPAddress.Parse("46.174.54.84"), 27015);
        var serverEndpoint4 = new IPEndPoint(IPAddress.Parse("164.132.202.2"), 27018);
        var serverEndpoint5 = new IPEndPoint(IPAddress.Parse("173.199.107.143"), 7780);



        var infoTask1 = connPool.GetInfoAsync(serverEndpoint1);
        var infoTask2 = connPool.GetInfoAsync(serverEndpoint2);
        var infoTask3 = connPool.GetInfoAsync(serverEndpoint3);
        var infoTask4 = connPool.GetInfoAsync(serverEndpoint4);
        var infoTask5 = connPool.GetInfoAsync(serverEndpoint5);
        await Task.WhenAll(infoTask1, infoTask2, infoTask3, infoTask4, infoTask5);
        var (info1, info2, info3, info4, info5) = (await infoTask1, await infoTask2, await infoTask3, await infoTask4, await infoTask5);
        
        Console.WriteLine($"Server info: {info1}");
        Console.WriteLine($"Server info: {info2}");
        Console.WriteLine($"Server info: {info3}");
        Console.WriteLine($"Server info: {info4}");
        Console.WriteLine($"Server info: {info5}");
        Console.WriteLine($"Pool Info: Running: {connPool.Running} - Waiting: {connPool.WaitingForResponse} ");
        // If not using `using` statement

        connPool.Dispose();
    }
}