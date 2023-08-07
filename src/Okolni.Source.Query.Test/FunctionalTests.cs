using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Okolni.Source.Query;
using Okolni.Source.Common;
using Okolni.Source.Query.Source;

namespace Okolni.Source.Query.Test
{
    [TestClass]
    
    public class FunctionalTests
    {
        [DataRow("51.79.37.206", 2303)]
        [DataRow("168.100.162.254", 28015)]
        [DataRow("154.16.128.32", 28015)]
        [DataRow("164.132.202.2", 27018)]
        [DataRow("173.199.107.143", 7780)]
        [DataRow("216.52.148.47", 27015)]

        public void QueryTestSync(string Host, int Port)
        {
            using IQueryConnection conn = new QueryConnection();

            conn.Host = Host;
            conn.Port = Port;
            conn.ReceiveTimeout = 5000;
            conn.SendTimeout = 5000;

            conn.Setup();
            var players = conn.GetPlayers();
            var rules = conn.GetRules();
            var info = conn.GetInfo();
            Assert.IsNotNull(players);
            Assert.IsNotNull(rules);
            Assert.IsNotNull(info);
            conn.Dispose();
        }

        [TestMethod]
        [DataRow("51.79.37.206", 2303)]
        [DataRow("168.100.162.254", 28015)]
        [DataRow("154.16.128.32", 28015)]
        [DataRow("164.132.202.2", 27018)]
        [DataRow("173.199.107.143", 7780)]
        [DataRow("216.52.148.47", 27015)]
        public async Task QueryTestASync(string Host, int Port)
        {
            using IQueryConnection conn = new QueryConnection();
            conn.Host = Host;
            conn.Port = Port;
            conn.ReceiveTimeout = 5000;
            conn.SendTimeout = 5000;

            conn.Setup();
            var players = await conn.GetPlayersAsync();
            var rules = await conn.GetRulesAsync();
            var info = await conn.GetInfoAsync();
            Assert.IsNotNull(players);
            Assert.IsNotNull(rules);
            Assert.IsNotNull(info);
            conn.Dispose();
        }
        [TestMethod]
        public async Task QueryTestPool()
        {
            using IQueryConnectionPool connPool = new QueryConnectionPool();
            connPool.Error += exception => throw exception;
            connPool.ReceiveTimeout = 5000;
            connPool.SendTimeout = 5000;
            connPool.Setup();
            var serverEndpoint1 = new IPEndPoint(IPAddress.Parse("51.79.37.206"), 2303);
            var serverEndpoint2 = new IPEndPoint(IPAddress.Parse("104.143.3.125"), 28010);
            var serverEndpoint3 = new IPEndPoint(IPAddress.Parse("64.40.8.96"), 28015);
            var serverEndpoint4 = new IPEndPoint(IPAddress.Parse("164.132.202.2"), 27018);
            var serverEndpoint5 = new IPEndPoint(IPAddress.Parse("173.199.107.143"), 7780);

            var infoTask1 = connPool.GetInfoAsync(serverEndpoint1);
            var infoTask2 = connPool.GetInfoAsync(serverEndpoint2);
            var infoTask3 = connPool.GetInfoAsync(serverEndpoint3);
            var infoTask4 = connPool.GetInfoAsync(serverEndpoint4);
            var infoTask5 = connPool.GetInfoAsync(serverEndpoint5);
            await Task.WhenAll(infoTask1, infoTask2, infoTask3, infoTask4, infoTask5);
            var (info1, info2, info3, info4, info5) = (infoTask1.Result, infoTask2.Result, infoTask3.Result, infoTask4.Result, infoTask5.Result);
            Assert.IsNotNull(info1);
            Assert.IsNotNull(info2);
            Assert.IsNotNull(info3);
            Assert.IsNotNull(info4);
            Assert.IsNotNull(info5);
            connPool.Dispose();
        }

        [TestMethod]
        //192.0.2.0/24 is reserved documentation range
        [DataRow("192.0.2.0", 27015)]
        public async Task QueryTestPoolFailCondition(string Host, int Port)
        {
            using IQueryConnectionPool connPool = new QueryConnectionPool();
            connPool.Error += exception => throw exception; 
            connPool.ReceiveTimeout = 5000;
            connPool.SendTimeout = 5000;
            connPool.Setup();

            await Assert.ThrowsExceptionAsync<SourceQueryException>(async () =>
                await connPool.GetInfoAsync(new IPEndPoint(IPAddress.Parse(Host), Port)));
        }
    }
}
