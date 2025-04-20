using System.Net;
using System.Net.Sockets;
using System.Text;
using project2.Enums;
using project2.Utils;
using project2.Interfaces;
using Microsoft.Extensions.Logging;

namespace project2.Models;

public class UdpMessageClient(
    string server,
    int port,
    ILogger<UdpMessageClient> logger,
    int maxUdpRetransmissions,
    ushort udpConfirmationTimeout) : IMessageClient
{
    private UdpClient? _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private CancellationToken _token;
    private readonly User _user = new();
    private readonly MessageBuffer<UdpMessage> _messageBuffer = new();
    private ClientState _state;
    private bool _waitingForAuthReply;
    private bool _waitingForJoinReply;
    private int _pendingConfirmationId = -1;
    private ushort _currentMessageId;
    private IPEndPoint _remoteEndpoint = new(IPAddress.Any, 0);
    private bool _usingDynamicPort;
    private HashSet<ushort> _processedIds = new HashSet<ushort>();
    private int _maxUdpRetransmissions = maxUdpRetransmissions;
    private ushort _udpConfirmationTimeout = udpConfirmationTimeout;


    public void StartCommunication()
    {
        try
        {
            _token = _cts.Token;

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _messageBuffer.Add(new UdpMessage()
                {
                    Type = UdpMessageType.Bye,
                    DisplayName = _user.DisplayName
                });
                logger.LogInformation("Ctrl+C detected — BYE message enqueued.");
            };

            // Resolve ip adress
            if (!IPAddress.TryParse(server, out var ipAddress))
            {
                // If server is a URL, resolve it to an IP
                IPHostEntry hostEntry = Dns.GetHostEntry(server);
                ipAddress = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipAddress == null)
                    throw new Exception("ERROR: No IPv4 address found for the specified host.");
            }

            _remoteEndpoint = new IPEndPoint(ipAddress, port);
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

            _state = ClientState.Start;

            _ = Task.Run(ReceiveMessagesAsync, _token);
            _ = Task.Run(SendBufferedMessagesAsync, _token);
            ProcessUserInput();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        finally
        {
            EndCommunication(0);
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        if (_udpClient == null) throw new InvalidOperationException("ERROR: Not connected to a server.");
        while (!_token.IsCancellationRequested)
        {
            try
            {
                UdpReceiveResult result = await _udpClient.ReceiveAsync();
                byte[] packet = result.Buffer;
                UdpMessageType messageType = (UdpMessageType)packet[0];
                Dictionary<string, object> data = Deserialize(packet);

                if (data.ContainsKey("MessageID"))
                {
                    if (_processedIds.Contains((ushort)data["MessageID"]))
                    {
                        SendConfirmationPacket((ushort)data["MessageID"]);
                        continue;
                    }
                    // in reply can be changed remote ip so there is sending after
                    if (messageType != UdpMessageType.Reply)
                    {
                        _processedIds.Add((ushort)data["MessageID"]);
                        SendConfirmationPacket((ushort)data["MessageID"]);
                    }
                }

                switch (messageType)
                {
                    case UdpMessageType.Reply:
                        logger.LogInformation("getting reply");
                        ProcessReply(packet, result);
                        break;
                    case UdpMessageType.Msg:
                        logger.LogInformation("getting msg");
                        ProcessMsg(packet);
                        break;
                    case UdpMessageType.Ping:
                        break;
                    case UdpMessageType.Bye:
                        EndCommunication(0);
                        break;
                    case UdpMessageType.Err:
                        Console.WriteLine($"ERROR FROM {data["DisplayName"]}: {data["MessageContents"]}");
                        EndCommunication(1);
                        break;
                    case UdpMessageType.Confirm:
                        if (packet.Length != 3)
                        {
                            Console.WriteLine("ERROR: received malformed packet");
                            EndCommunication(1);
                        }

                        var refId = (ushort)((packet[1] << 8) | packet[2]);
                        if (_pendingConfirmationId == refId)
                        {
                            _pendingConfirmationId = -1;
                        }

                        break;
                    default:
                        SendErrAndEndCommunication("ERROR: Recieved invalid packet");
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }
    }

    private void ProcessMsg(byte[] packet)
    {
        if (_state == ClientState.Start || _state == ClientState.Auth)
        {
            SendErrAndEndCommunication("ERROR: recieved message in wrong state");
        }

        var message = Deserialize(packet);

        Console.WriteLine($"{message["DisplayName"]}: {message["MessageContents"]}");
    }

    private void ProcessReply(byte[] packet, UdpReceiveResult result)
    {
        var replyMessage = Deserialize(packet);

        //switch to new port
        if (!_usingDynamicPort)
        {
            _remoteEndpoint = result.RemoteEndPoint;
            _usingDynamicPort = true;
        }

        int resultByte = (int)replyMessage["Result"];
        if (resultByte == 1)
        {
            Console.WriteLine($"Action Success: {replyMessage["MessageContents"]}");
            if (_state == ClientState.Auth || _state == ClientState.Start)
            {
                _user.SetIsAuthenticated(true);
            }

            _state = ClientState.Open;
        }
        else
        {
            Console.WriteLine($"Action Failure: {replyMessage["MessageContents"]}");
            if (_state == ClientState.Join)
            {
                _state = ClientState.Open;
            }
        }

        SendConfirmationPacket((ushort)replyMessage["MessageID"]);

        _waitingForAuthReply = false;
        _waitingForJoinReply = false;
    }

    private void SendConfirmationPacket(ushort confirmingId)
    {
        logger.LogInformation("sending confirmation");
        var confirmMessage = new UdpMessage();
        confirmMessage.DisplayName = _user.DisplayName;
        confirmMessage.Type = UdpMessageType.Confirm;

        var confirmPacket = confirmMessage.Serialize(confirmingId);

        _udpClient!.SendAsync(confirmPacket, confirmPacket.Length, _remoteEndpoint);
    }

    private void ProcessUserInput()
    {
        while (!_token.IsCancellationRequested)
        {
            logger.LogInformation("status: " + _state);

            string? input = Console.ReadLine();

            if (input == null)
            {
                logger.LogInformation("ctrl +d pressed");
                _messageBuffer.Add(new UdpMessage()
                {
                    Type = UdpMessageType.Bye,
                    DisplayName = _user.DisplayName
                });
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            // segment user input into parts and parse
            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();
            switch (command)
            {
                case "/auth":
                    _messageBuffer.Add(new UdpMessage
                    {
                        Type = UdpMessageType.Auth,
                        MessageArgs = args
                    });
                    break;
                case "/join":
                    _messageBuffer.Add(new UdpMessage
                    {
                        Type = UdpMessageType.Join,
                        MessageArgs = args,
                        DisplayName = _user.DisplayName
                    });
                    break;
                case "/rename":
                    string error;
                    _user.SetDisplayName(args[0], out error);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Console.WriteLine(error);
                    }

                    break;

                case "/help":
                    Console.WriteLine("""
                                      /auth {Username} {Secret} {DisplayName}     Sends AUTH message with the data provided from the command to the server (and correctly handles the Reply message), locally sets the DisplayName value (same as the /rename command)
                                      /join {ChannelID}                          Sends JOIN message with channel name from the command to the server (and correctly handles the Reply message)
                                      /rename {DisplayName}                      Locally changes the display name of the user to be sent with new messages/selected commands
                                      /help                                      Prints out supported local commands with their parameters and a description
                                      """);
                    break;
                default:
                    if (command.StartsWith("/"))
                    {
                        Console.WriteLine($"ERROR: Invalid command '{command}'");
                        break;
                    }

                    var message = new UdpMessage()
                    {
                        Type = UdpMessageType.Msg,
                        MessageArgs = [input],
                        DisplayName = _user.DisplayName
                    };

                    _messageBuffer.Add(message);
                    break;
            }
        }
        
        while (true)
        {
            //wait until all things are closed
            Thread.Sleep(100);
        }
        
    }

    public Dictionary<string, object> Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        byte typeByte = reader.ReadByte();
        ushort messageId;
        Dictionary<string, object> result = new()
        {
            ["Type"] = (UdpMessageType)typeByte
        };
        logger.LogInformation($"type: {(UdpMessageType)typeByte}");
        switch ((UdpMessageType)typeByte)
        {
            case UdpMessageType.Confirm:
                result["RefMessageID"] = ReadUInt16(reader);
                break;

            case UdpMessageType.Reply:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["Result"] = (int)reader.ReadByte();
                result["RefMessageID"] = ReadUInt16(reader);
                result["MessageContents"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.Auth:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["Username"] = ReadZeroTerminatedString(reader);
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                result["Secret"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.Join:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["ChannelID"] = ReadZeroTerminatedString(reader);
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.Msg:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                result["MessageContents"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.Err:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                result["MessageContents"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.Bye:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.Ping:
                result["MessageID"] = ReadUInt16(reader);
                break;

            default:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                break;
        }

        return result;
    }

    private ushort ReadUInt16(BinaryReader reader) =>
        BitConverter.ToUInt16(reader.ReadBytes(2).Reverse().ToArray(), 0);

    private string ReadZeroTerminatedString(BinaryReader reader)
    {
        List<byte> bytes = new();
        byte b;
        while ((b = reader.ReadByte()) != 0)
            bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    // Sends the messages stored in the buffer
    private async Task SendBufferedMessagesAsync()
    {
        while (!_token.IsCancellationRequested)
        {
            if (_messageBuffer.IsEmpty)
            {
                await Task.Delay(100);
                continue;
            }

            var message = _messageBuffer.Peek();

            bool allowedToSend = message != null && message.Type switch
            {
                UdpMessageType.Msg => !_waitingForAuthReply && !_waitingForJoinReply,
                _ => true
            };

            if (!allowedToSend)
            {
                await Task.Delay(50, _token);
                continue;
            }

            logger.LogInformation("entering sending switch");
            if (message != null)
                switch (message.Type)
                {
                    case UdpMessageType.Auth:
                        if (_user.IsAuthenticated)
                        {
                            Console.WriteLine("ERROR: Already authenticated");
                            break;
                        }

                        if (message.MessageArgs.Length != 3)
                        {
                            Console.WriteLine("ERROR: Usage: /auth <username> <secret> <displayName>");
                            break;
                        }

                        if (!_user.SetUsername(message.MessageArgs[0], out var error) ||
                            !_user.SetSecret(message.MessageArgs[1], out error) ||
                            !_user.SetDisplayName(message.MessageArgs[2], out error))
                        {
                            Console.WriteLine(error);
                            break;
                        }

                        _state = ClientState.Auth;
                        _messageBuffer.TryGet(out _);
                        _waitingForAuthReply = true;
                        await SendUdpMessage(message);
                        break;

                    case UdpMessageType.Msg:
                        if (!_user.IsAuthenticated)
                        {
                            Console.WriteLine("ERROR: You must be authenticated before sending messages.\n");
                            break;
                        }

                        if (_state != ClientState.Open)
                        {
                            Console.WriteLine("ERROR: cannot send message ");
                            break;
                        }

                        logger.LogInformation("sending msg");
                        await SendUdpMessage(message);
                        break;

                    case UdpMessageType.Join:
                        if (!_user.IsAuthenticated)
                        {
                            Console.WriteLine("ERROR: You must be authenticated before joining a channel.\n");
                            break;
                        }

                        _state = ClientState.Join;
                        _waitingForJoinReply = true;

                        logger.LogInformation("sending msg");
                        await SendUdpMessage(message);
                        break;

                    case UdpMessageType.Bye:
                        logger.LogInformation("sending bye");
                        await SendUdpMessage(message);

                        EndCommunication(0);
                        break;
                    case UdpMessageType.Err:
                        logger.LogInformation("sending err");
                        await SendUdpMessage(message);

                        EndCommunication(1);
                        break;
                }

            _messageBuffer.TryGet(out _);
        }
    }

    private async Task SendUdpMessage(UdpMessage message)
    {
        _pendingConfirmationId = _currentMessageId;

        var packet = message.Serialize(_currentMessageId);
        
        logger.LogInformation("packet sent");
        int retransmissionCount = 0;
        bool confirmed = false;

        while (retransmissionCount <= _maxUdpRetransmissions && !confirmed)
        {
            // Send the packet
            await _udpClient!.SendAsync(packet, packet.Length, _remoteEndpoint);

            // Wait for confirmation (using timeout)
            var confirmationTask = Task.Run(async () =>
            {
                while (_pendingConfirmationId == _currentMessageId)
                {
                    await Task.Delay(10); // Small delay to prevent CPU hogging
                }

                return _pendingConfirmationId == -1;
            });

            // Wait for either confirmation or timeout
            if (await Task.WhenAny(confirmationTask, Task.Delay(_udpConfirmationTimeout)) == confirmationTask)
            {
                // Confirmation received
                confirmed = await confirmationTask;
                break;
            }

            // Timeout occurred, increment retransmission counter
            retransmissionCount++;
        }

        if (!confirmed && retransmissionCount > _maxUdpRetransmissions)
        {
            Console.WriteLine(
                $"ERROR: UDP message {_currentMessageId} failed after {_maxUdpRetransmissions} retransmission attempts");
            _pendingConfirmationId = -1;
            _currentMessageId++;
            EndCommunication(1);
        }

        _currentMessageId++;
    }

    private async void SendErrAndEndCommunication(string reason)
    {
        try
        {
            var errorMessage = new UdpMessage()
            {
                Type = UdpMessageType.Err,
                DisplayName = _user.DisplayName,
                MessageArgs = ["Invalid message"]
            };

            Console.WriteLine(reason);
            await SendUdpMessage(errorMessage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send ERR message.");
        }
        finally
        {
            EndCommunication(1);
        }
    }

    private void EndCommunication(int errorCode)
    {
        _udpClient?.Dispose();
        Environment.Exit(errorCode);
    }
}