using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Okolni.Source.Query.Pool.Common;
using Okolni.Source.Query.Source;
using Okolni.Source.Query.Test.Fakes;

// Local load benchmark: many fake game servers on loopback, queried the way the Steam Collector polls them
// (info, then rules, then players, per server) through one shared QueryConnectionPool.
// Usage: dotnet run -c Release --project bench/Okolni.Source.Query.Bench -- [servers] [rounds] [concurrency] [deadPercent] [timeoutMs]

var serverCount = ArgOrDefault(0, 2000);
var rounds = ArgOrDefault(1, 5);
var concurrency = ArgOrDefault(2, 1500);
var deadPercent = ArgOrDefault(3, 20);
var timeoutMs = ArgOrDefault(4, 500);
const int maxRetries = 3;

Console.WriteLine(
    $"servers={serverCount} rounds={rounds} concurrency={concurrency} dead={deadPercent}% timeout={timeoutMs}ms retries={maxRetries}");

var rules = Enumerable.Range(0, 60).ToDictionary(i => $"description_{i:00}", i => new string('x', 30));
var players = Enumerable.Range(0, 80).Select(i => new FakePlayer($"player{i}", i, i)).ToList();
var servers = Enumerable.Range(0, serverCount).Select(i => new FakeA2SServer
{
    Silent = i * 100 / serverCount < deadPercent,
    InfoRequiresChallenge = i % 2 == 0,
    Rules = rules,
    Players = players,
    MultiPacketSize = 1200
}).ToList();

var pool = new QueryConnectionPool();
pool.ReceiveTimeout = timeoutMs;
pool.SendTimeout = timeoutMs;
pool.Setup();

// Like a collector poll, each server is queried once per round (the pool allows one query per endpoint at a time)
var work = new ConcurrentQueue<IPEndPoint>();
var totalPolls = serverCount * rounds;
var latencies = new ConcurrentBag<double>();
var succeeded = 0;
var failed = 0;

GC.Collect();
var allocatedBefore = GC.GetTotalAllocatedBytes(true);
var cpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
var wall = Stopwatch.StartNew();

var watchdog = Task.Delay(TimeSpan.FromMinutes(3));
for (var round = 0; round < rounds; round++)
{
    foreach (var server in servers)
        work.Enqueue(server.EndPoint);
    var roundTask = Task.WhenAll(Enumerable.Range(0, Math.Min(concurrency, serverCount)).Select(_ => Task.Run(Worker)));
    if (await Task.WhenAny(roundTask, watchdog) == watchdog)
    {
        Console.WriteLine($"HUNG: round {round} did not finish within 3 minutes, waiting={pool.WaitingForResponse} running={pool.Running}");
        return 1;
    }
}

wall.Stop();
var cpu = Process.GetCurrentProcess().TotalProcessorTime - cpuBefore;
var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;

await pool.DisposeAsync(10);
foreach (var server in servers)
    await server.DisposeAsync();

var sorted = latencies.Order().ToArray();
Console.WriteLine($"polls={totalPolls} ok={succeeded} failed={failed}");
Console.WriteLine($"wall={wall.Elapsed.TotalSeconds:F2}s throughput={totalPolls / wall.Elapsed.TotalSeconds:F0} polls/s");
Console.WriteLine($"cpu={cpu.TotalSeconds:F2}s allocated={allocated / 1024.0 / 1024.0:F1}MB");
if (sorted.Length > 0)
    Console.WriteLine($"ok latency p50={Percentile(0.50):F1}ms p99={Percentile(0.99):F1}ms");
Console.WriteLine($"arrayPoolNotReturned={ArrayPoolInterface.NotReturnedArrays}");
return 0;

async Task Worker()
{
    while (work.TryDequeue(out var endPoint))
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await pool.GetInfoAsync(endPoint, maxRetries);
            await pool.GetRulesAsync(endPoint, maxRetries);
            await pool.GetPlayersAsync(endPoint, maxRetries);
            latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Interlocked.Increment(ref succeeded);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref failed);
        }
    }
}

double Percentile(double p) => sorted[(int)Math.Min(sorted.Length - 1, Math.Floor(p * sorted.Length))];

int ArgOrDefault(int index, int fallback) => args.Length > index && int.TryParse(args[index], out var v) ? v : fallback;
