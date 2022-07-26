﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Common;
using Okolni.Source.Common.ByteHelper;
using Okolni.Source.Query.Common.SocketHelpers;
using Okolni.Source.Query.Responses;

namespace Okolni.Source.Query.Common;

internal static class QueryHelper
{
    private static async Task Request(byte[] requestMessage, IPEndPoint endPoint, ISocket socket, int SendTimeout)
    {
        var newCancellationToken = new CancellationTokenSource();
        newCancellationToken.CancelAfter(SendTimeout);
        try
        {
            await socket.SendToAsync(new ReadOnlyMemory<byte>(requestMessage), SocketFlags.None, endPoint,
                newCancellationToken.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Send Request took longer then the specified timeout {SendTimeout}");
        }
    }

    private static async Task<byte[]> ReceiveAsync(IPEndPoint endPoint, ISocket socket, int ReceiveTimeout)
    {
        var newCancellationToken = new CancellationTokenSource();
        newCancellationToken.CancelAfter(ReceiveTimeout);
        try
        {
            var udpClientReceive =
                await socket.ReceiveFromAsync(SocketFlags.None, endPoint, newCancellationToken.Token);
            return udpClientReceive.ToArray();
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Receive took longer then the specified timeout {ReceiveTimeout}");
        }
    }

    private static async Task<byte[]> FetchResponse(IPEndPoint endPoint, ISocket socket, int ReceiveTimeout)
    {
        var response = await ReceiveAsync(endPoint, socket, ReceiveTimeout);
        var byteReader = response.GetByteReader();
        var header = byteReader.GetUInt();
        if (header.Equals(Constants.SimpleResponseHeader))
            return byteReader.GetRemaining();
        return await FetchMultiPacketResponse(byteReader, endPoint, socket, ReceiveTimeout);
    }

    private static async Task<byte[]> FetchMultiPacketResponse(IByteReader byteReader, IPEndPoint endPoint,
        ISocket socket, int ReceiveTimeout)
    {
        var firstResponse = new MultiPacketResponse
        {
            Id = byteReader.GetUInt(), Total = byteReader.GetByte(), Number = byteReader.GetByte(),
            Size = byteReader.GetShort(), Payload = byteReader.GetRemaining()
        };

        var compressed = (firstResponse.Id & 2147483648) == 2147483648; // Check for compression

        var responses = new List<MultiPacketResponse>(new[] { firstResponse });
        for (var i = 1; i < firstResponse.Total; i++)
        {
            var response = await ReceiveAsync(endPoint, socket, ReceiveTimeout);
            var multiResponseByteReader = response.GetByteReader();
            var header = multiResponseByteReader.GetUInt();
            if (header != Constants.MultiPacketResponseHeader)
            {
                i--;
                continue;
            }

            var id = multiResponseByteReader.GetUInt();
            if (id != firstResponse.Id)
            {
                i--;
                continue;
            }

            responses.Add(new MultiPacketResponse
            {
                Id = id, Total = multiResponseByteReader.GetByte(), Number = multiResponseByteReader.GetByte(),
                Size = multiResponseByteReader.GetShort(), Payload = multiResponseByteReader.GetRemaining()
            });
        }

        var assembledPayload = AssembleResponses(responses);
        var assembledPayloadByteReader = assembledPayload.GetByteReader();

        if (compressed)
            throw new NotImplementedException("Compressed responses are not yet implemented");

        var payloadHeader = assembledPayloadByteReader.GetUInt();

        return assembledPayloadByteReader.GetRemaining();
    }

    private static byte[] AssembleResponses(IEnumerable<MultiPacketResponse> responses)
    {
        responses = responses.OrderBy(x => x.Number);
        var assembledPayload = new List<byte>();

        foreach (var response in responses) assembledPayload.AddRange(response.Payload);

        return assembledPayload.ToArray();
    }


    /// <summary>
    ///     Gets the servers general information
    /// </summary>
    /// <returns>InfoResponse containing all Infos</returns>
    /// <exception cref="SourceQueryException"></exception>
    public static async Task<InfoResponse> GetInfoAsync(IPEndPoint endPoint, ISocket socket, int sendTimeout,
        int receiveTimeout, int maxRetries)
    {
        try
        {
            (var byteReader, var header, int retriesUsed) = await RequestDataFromServer(Constants.A2S_INFO_REQUEST, endPoint, socket, maxRetries,
                sendTimeout, receiveTimeout);

            if (header == Constants.A2S_INFO_RESPONSE_GOLDSOURCE)
                throw new ArgumentException("Obsolete GoldSource Response are not supported right now");

            if (header != Constants.A2S_INFO_RESPONSE)
                throw new ArgumentException("The fetched Response is no A2S_INFO Response.");

            var res = new InfoResponse()
            {
                Header = header,
                Retries = retriesUsed,
                Protocol = byteReader.GetByte(),
                Name = byteReader.GetString(),
                Map = byteReader.GetString(),
                Folder = byteReader.GetString(),
                Game = byteReader.GetString(),
                ID = byteReader.GetUShort(),
                Players = byteReader.GetByte(),
                MaxPlayers = byteReader.GetByte(),
                Bots = byteReader.GetByte(),
                ServerType = byteReader.GetByte().ToServerType(),
                Environment = byteReader.GetByte().ToEnvironment(),
                Visibility = byteReader.GetByte().ToVisibility(),
                VAC = byteReader.GetByte() == 0x01
            };

            res.Version = byteReader.GetString();

            //IF Has EDF Flag 
            if (byteReader.Remaining > 0)
            {
                res.EDF = byteReader.GetByte();

                if ((res.EDF & Constants.EDF_PORT) == Constants.EDF_PORT) res.Port = byteReader.GetUShort();
                if ((res.EDF & Constants.EDF_STEAMID) == Constants.EDF_STEAMID) res.SteamID = byteReader.GetULong();
                if ((res.EDF & Constants.EDF_SOURCETV) == Constants.EDF_SOURCETV)
                {
                    res.SourceTvPort = byteReader.GetUShort();
                    res.SourceTvName = byteReader.GetString();
                }

                if ((res.EDF & Constants.EDF_KEYWORDS) == Constants.EDF_KEYWORDS) res.KeyWords = byteReader.GetString();
                if ((res.EDF & Constants.EDF_GAMEID) == Constants.EDF_GAMEID) res.GameID = byteReader.GetULong();
            }

            return res;
        }
        catch (Exception ex)
        {
            throw new SourceQueryException($"Could not gather Info for {endPoint}", ex);
        }
    }


    /// <summary>
    ///     Gets all active players on a server
    /// </summary>
    /// <returns>PlayerResponse containing all players </returns>
    /// <exception cref="SourceQueryException"></exception>
    public static async Task<PlayerResponse> GetPlayersAsync(IPEndPoint endPoint, ISocket socket, int sendTimeout,
        int ReceiveTimeout, int maxRetries = 10)
    {
        try
        {
            (var byteReader, var header, int retriesUsed) = await RequestDataFromServer(Constants.A2S_PLAYER_CHALLENGE_REQUEST, endPoint, socket,
                maxRetries, sendTimeout, ReceiveTimeout, true);

         

            if (!header.Equals(Constants.A2S_PLAYER_RESPONSE))
                throw new ArgumentException("Response was no player response.");

            var playerResponse = new PlayerResponse { Header = header, Retries = retriesUsed, Players = new List<Player>() };
            int playercount = byteReader.GetByte();
            while (byteReader.Remaining > 9) // Min player obj is 9 bytes.. playercount can't be trusted as it maxes out at 255, Rust & others can have more players.
                playerResponse.Players.Add(new Player
                {
                    Index = byteReader.GetByte(),
                    Name = byteReader.GetString(),
                    Score = byteReader.GetUInt(),
                    Duration = TimeSpan.FromSeconds(byteReader.GetFloat())
                });

            // Hack to try to fix errors with corrupted players...
            if (playerResponse.Players.Count > 255)
            {
                // Unexpected, probably corruption...
                playerResponse.Players.RemoveAll(player => string.IsNullOrWhiteSpace(player.Name));
            }

            return playerResponse;
        }
        catch (Exception ex)
        {
            throw new SourceQueryException($"Could not gather Players for {endPoint}", ex);
        }
    }


    /// <summary>
    ///     Gets the rules of the server
    /// </summary>
    /// <returns>RuleResponse containing all rules as a Dictionary</returns>
    /// <exception cref="SourceQueryException"></exception>
    public static async Task<RuleResponse> GetRulesAsync(IPEndPoint endPoint, ISocket socket, int sendTimeout,
        int receiveTimeout, int maxRetries = 10)
    {
        try
        {
            (var byteReader, var header, int retriesUsed) = await RequestDataFromServer(Constants.A2S_RULES_CHALLENGE_REQUEST, endPoint, socket,
                maxRetries, sendTimeout, receiveTimeout, true);


            if (!header.Equals(Constants.A2S_RULES_RESPONSE))
                throw new ArgumentException("Response was no rules response.");

            var ruleResponse = new RuleResponse { Header = header, Retries = retriesUsed, Rules = new Dictionary<string, string>() };
            int rulecount = byteReader.GetShort();
            for (var i = 1; i <= rulecount; i++) ruleResponse.Rules.Add(byteReader.GetString(), byteReader.GetString());

            return ruleResponse;
        }
        catch (Exception ex)
        {
            throw new SourceQueryException($"Could not gather Rules for {endPoint}", ex);
        }
    }

    // This could do with some clean up
    public static async Task<(IByteReader reader, byte header, int retriesUsed)> RequestDataFromServer(byte[] request,
        IPEndPoint endPoint, ISocket socket, int maxRetries, int sendTimeout, int ReceiveTimeout,
        bool replaceLastBytesInRequest = false)
    {
        var retries = 0;
        // Always try at least once...
        do
        {
            try
            {
                await Request(request, endPoint, socket, sendTimeout);
                var response = await FetchResponse(endPoint, socket, ReceiveTimeout);

                var byteReader = response.GetByteReader();
                var header = byteReader.GetByte();

                if (header == Constants
                        .CHALLENGE_RESPONSE) // Header response is a challenge response so the challenge must be sent as well
                {
                    do
                    {
                        var retryRequest = request;
                        // Note for future: Nothing guarantees the A2S_Info Challenge will always be 4 bytes. A2S_Players and A2S_Rules Challenge length is defined in spec/dev docs, but not A2S_Info.
                        var challenge = byteReader.GetBytes(4);
                        if (replaceLastBytesInRequest)
                            retryRequest.InsertArray(retryRequest.Length - 4, challenge);
                        else
                            Helper.AppendToArray(ref retryRequest, challenge);

                        await Request(retryRequest, endPoint, socket, sendTimeout);

                        var retryResponse = await FetchResponse(endPoint, socket, ReceiveTimeout);
                        byteReader = retryResponse.GetByteReader();
                        header = byteReader.GetByte();
                        if (header == Constants.CHALLENGE_RESPONSE)
                            retries++;
                    } while (header == Constants.CHALLENGE_RESPONSE && retries < maxRetries);

                    if (header == Constants.CHALLENGE_RESPONSE)
                        throw new SourceQueryException(
                            $"Retry limit exceeded for the request.  Tried {retries} times, but couldn't get non-challenge packet.");
                }

#if DEBUG
                    if (retries > 0)
                    {
                        Console.WriteLine($"Took {retries} retries for {endPoint}....");
                    }
#endif

                return (byteReader, header, retries);
            }
            // Any timeout is just another signal to continue
            catch (TimeoutException)
            {
                /* Nom */
            }
            finally
            {
                // Intellij gets confused, keep in mind above we are returning the byteReader and header if everything else is successful
                retries++;
            }
        } while (retries < maxRetries);

        throw new SourceQueryException($"Retry limit exceeded for the request. Tried {retries} times.");
    }
}