using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Okolni.Source.Common;
using Okolni.Source.Common.ByteHelper;
using Okolni.Source.Query.Common.SocketHelpers;
using Okolni.Source.Query.Pool.Common;
using Okolni.Source.Query.Responses;
using static System.Runtime.InteropServices.JavaScript.JSType;
using String = System.String;

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
            return udpClientReceive;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Receive took longer then the specified timeout {ReceiveTimeout}");
        }
    }

    private static async Task<(Memory<byte> response, byte[] baseArray)> FetchResponse(IPEndPoint endPoint, ISocket socket, int ReceiveTimeout)
    {
        var response = await ReceiveAsync(endPoint, socket, ReceiveTimeout);
        var byteReader = response.GetByteReader();
        var header = byteReader.GetUInt();
        if (header.Equals(Constants.SimpleResponseHeader))
            return (byteReader.GetRemaining(), response);
        return await FetchMultiPacketResponse(response, byteReader, endPoint, socket, ReceiveTimeout);
    }

    private static async Task<(Memory<byte> response, byte[] baseArray)> FetchMultiPacketResponse(byte[] incArray, IByteReader byteReader, IPEndPoint endPoint,
        ISocket socket, int ReceiveTimeout)
    {
        var firstResponse = new MultiPacketResponse
        {
            Id = byteReader.GetUInt(), Total = byteReader.GetByte(), Number = byteReader.GetByte(),
            Size = byteReader.GetShort(), Payload = byteReader.GetRemaining(), ReceivedByteArrayRef = incArray
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
                Size = multiResponseByteReader.GetShort(), Payload = multiResponseByteReader.GetRemaining(),
                ReceivedByteArrayRef = response,
            });
        }

        var assembledPayload = AssembleResponses(responses);
        var assembledPayloadByteReader = assembledPayload.GetByteReader();

        if (compressed)
            throw new NotImplementedException("Compressed responses are not yet implemented");

        _ = assembledPayloadByteReader.GetUInt();

        return (assembledPayloadByteReader.GetRemaining(), assembledPayload);
    }

    private static byte[] AssembleResponses(List<MultiPacketResponse> responses)
    {

        // determine required size
        int totalSize = responses.Sum(r => r.Payload.Length);

        // create a single buffer of the required size

        byte[] assembledPayload = ArrayPoolInterface.Rent(totalSize);
        int offset = 0;

        foreach (var response in responses.OrderBy(x => x.Number))
        {
            // copy the bytes into the buffer
            response.Payload.Span.CopyTo(assembledPayload.AsSpan(offset));
            ArrayPoolInterface.Return(response.ReceivedByteArrayRef);
            // increment the offset 
            offset += response.Payload.Length;
        }

        return assembledPayload;
    }



    /// <summary>
    ///     Gets the servers general information
    /// </summary>
    /// <returns>InfoResponse containing all Infos</returns>
    /// <exception cref="SourceQueryException"></exception>
    public static async Task<InfoResponse> GetInfoAsync(IPEndPoint endPoint, ISocket socket, int sendTimeout,
        int receiveTimeout, int maxRetries)
    {
        IByteReader byteReader = null;
        try
        {
            await socket.Setup();
            (byteReader, var header, int retriesUsed) = await RequestDataFromServer(Constants.A2S_INFO_REQUEST,
                endPoint, socket, maxRetries,
                sendTimeout, receiveTimeout, Constants.A2S_INFO_RESPONSE);

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
        finally
        {
            byteReader?.Dispose();
            await socket.DisposeAsync();
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
        IByteReader byteReader = null;
        try
        {
            await socket.Setup();
            (byteReader, var header, int retriesUsed) = await RequestDataFromServer(
                Constants.A2S_PLAYER_CHALLENGE_REQUEST, endPoint, socket,
                maxRetries, sendTimeout, ReceiveTimeout, Constants.A2S_PLAYER_RESPONSE, true);


            if (!header.Equals(Constants.A2S_PLAYER_RESPONSE))
                throw new ArgumentException("Response was no player response.");

            var playerResponse = new PlayerResponse
                { Header = header, Retries = retriesUsed, Players = new List<Player>() };
            var getPlayers = byteReader.GetByte();
            while
                (byteReader.Remaining >
                 9) // Min player obj is 9 bytes.. playercount can't be trusted as it maxes out at 255, Rust & others can have more players.
            {
                var getNewPlayer = new Player
                {
                    Index = byteReader.GetByte(),
                    Name = byteReader.GetString(),
                    Score = byteReader.GetUInt(),
                };
                float seconds = byteReader.GetFloat();

                if (seconds >= 0 && seconds <= TimeSpan.MaxValue.TotalSeconds)
                {
                    getNewPlayer.Duration = TimeSpan.FromSeconds(seconds);
                }
                // if everything is empty, this is probably just random junk
                if (String.IsNullOrEmpty(getNewPlayer.Name) && getNewPlayer.Index == 0 && getNewPlayer.Score == 0 &&
                    seconds == 0)
                    continue;
                playerResponse.Players.Add(getNewPlayer);
            }

            return playerResponse;
        }
        catch (Exception ex)
        {
            throw new SourceQueryException($"Could not gather Players for {endPoint}", ex);
        }
        finally
        {
            byteReader?.Dispose();
            await socket.DisposeAsync();
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
        IByteReader byteReader = null;
        try
        {
            await socket.Setup();
            (byteReader, var header, int retriesUsed) = await RequestDataFromServer(
                Constants.A2S_RULES_CHALLENGE_REQUEST, endPoint, socket,
                maxRetries, sendTimeout, receiveTimeout, Constants.A2S_RULES_RESPONSE, true);


            if (!header.Equals(Constants.A2S_RULES_RESPONSE))
                throw new ArgumentException("Response was no rules response.");

            var ruleResponse = new RuleResponse
                { Header = header, Retries = retriesUsed, Rules = new Dictionary<string, string>() };
            int rulecount = byteReader.GetShort();
            for (var i = 1; i <= rulecount; i++) ruleResponse.Rules.Add(byteReader.GetString(), byteReader.GetString());

            return ruleResponse;
        }
        catch (Exception ex)
        {
            throw new SourceQueryException($"Could not gather Rules for {endPoint}", ex);
        }
        finally
        {
            byteReader?.Dispose();
            await socket.DisposeAsync();
        }
    }


    public static void HandleChallenge(ref byte[] retryRequest, ReadOnlySpan<byte> challenge, bool replaceLastBytesInRequest)
    {
        return;
        if (replaceLastBytesInRequest)
        {
            int start = Math.Max(0, retryRequest.Length - 4);
            int maxlength = retryRequest.Length - start;
            if (challenge.Length > maxlength)
                challenge = challenge.Slice(0, challenge.Length - maxlength);

            Array.Copy(challenge.ToArray(), 0, retryRequest, start, challenge.Length);
        }
        else
        {
            int initialLength = retryRequest.Length;
            Array.Resize(ref retryRequest, retryRequest.Length + challenge.Length);
            Array.Copy(challenge.ToArray(), 0, retryRequest, initialLength, challenge.Length);
        }
    }




    // This could do with some clean up
    // headerWeWant was added specifically because, it looks like Path.net DDos Protected Servers (I'm thinking maybe their A2S Caching layer)? sometimes respond with A2S_INFO Replies to A2S_Player/A2S_Rules Queries...
    public static async Task<(IByteReader reader, byte header, int retriesUsed)> RequestDataFromServer(byte[] request,
        IPEndPoint endPoint, ISocket socket, int maxRetries, int sendTimeout, int ReceiveTimeout, byte headerWeWant,
        bool replaceLastBytesInRequest = false)
    {
        IByteReader byteReader = null;
        var retries = 0;
        // Always try at least once...
        do
        {
            try
            {
                await Request(request, endPoint, socket, sendTimeout);
                var response = await FetchResponse(endPoint, socket, ReceiveTimeout);

                byteReader = response.response.GetByteReader(response.baseArray);
                var header = byteReader.GetByte();

                if (header == Constants
                        .CHALLENGE_RESPONSE ||
                    header !=
                    headerWeWant) // Header response is a challenge response so the challenge must be sent as well
                {
                    do
                    {
                        var retryRequest = request;
                        // Note for future: Nothing guarantees the A2S_Info Challenge will always be 4 bytes. A2S_Players and A2S_Rules Challenge length is defined in spec/dev docs, but not A2S_Info.
                        var challenge = byteReader.GetBytes(4);
                        HandleChallenge(ref retryRequest, challenge.Span, replaceLastBytesInRequest);

                        await Request(retryRequest, endPoint, socket, sendTimeout);

                        var retryResponse = await FetchResponse(endPoint, socket, ReceiveTimeout);
                        byteReader.Dispose();
                        byteReader = retryResponse.response.GetByteReader(retryResponse.baseArray);
                        header = byteReader.GetByte();
                        if (header == Constants.CHALLENGE_RESPONSE || header != headerWeWant)
                            retries++;
#if DEBUG
                        if (header != headerWeWant && header != Constants.CHALLENGE_RESPONSE)
                        {
                            Console.WriteLine(
                                $"We got back a non-challenge response for {endPoint}, but it was {header}, not {headerWeWant}");
                        }
#endif
                    } while ((header == Constants.CHALLENGE_RESPONSE || header != headerWeWant) &&
                             retries < maxRetries);

                    if (header == Constants.CHALLENGE_RESPONSE)
                        throw new SourceQueryException(
                            $"Retry limit exceeded for the request.  Tried {retries} times, but couldn't get non-challenge packet.");

                    if (header != headerWeWant)
                        throw new SourceQueryException(
                            $"Retry limit exceeded for the request.  Tried {retries} times, but we only got the wrong header of {header} when we wanted {headerWeWant}.");
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
#if DEBUG
                Console.WriteLine($"Timeout for {endPoint}");
#endif
                /* Nom */
                byteReader?.Dispose();
            }
            catch (Exception)
            { // ensure disposal
                byteReader?.Dispose();
                throw;
            }
            finally
            {
                // Intellij gets confused, keep in mind above we are returning the byteReader and header if everything else is successful
                retries++;
            }
        } while (retries < maxRetries);
        byteReader?.Dispose();
        throw new SourceQueryException($"Retry limit exceeded for the request. Tried {retries} times.");
    }
}