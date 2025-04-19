using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using project2.Enums;
using project2.Utils;
using Microsoft.Extensions.Logging;

namespace project2.Models;

public class UdpMessageClient(string server, int port, ILogger<UdpMessageClient> logger)
{
    private UdpClient? _udpClient;
    private readonly CancellationTokenSource _cts = new();
    private CancellationToken _token;
    private readonly User _user = new();
    private readonly MessageBuffer<UdpMessage> _messageBuffer = new();
    private ClientState _state;
    private bool WaitingForAuthReply;
    private bool WaitingForJoinReply;
    private int _pendingConfirmationId = -1;
    private ushort _currentMessageId;
    private IPEndPoint _remoteEndpoint = new(IPAddress.Any, 0);
    private bool _usingDinamicPort;
    private HashSet<ushort> _processedIds = new HashSet<ushort>();

    //todo from console
    private int _maxUdpRetransmissions = 3;
    private ushort _udpConfirmationTimeout = 250;


    public void StartCommunication()
    {
        try
        {
            _token = _cts.Token;

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _messageBuffer.Add(new UdpMessage()
                {
                    Type = UdpMessageType.BYE,
                    DisplayName = _user.DisplayName
                });
                logger.LogInformation("Ctrl+C detected — BYE message enqueued.");
            };

            // Resolve ip adress
            IPAddress ipAddress;
            if (!IPAddress.TryParse(server, out ipAddress))
            {
                // If server is a URL, resolve it to an IP
                IPHostEntry hostEntry = Dns.GetHostEntry(server);
                ipAddress = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (ipAddress == null)
                    throw new Exception("ERROR: No IPv4 address found for the specified host.");
            }

            _remoteEndpoint = new IPEndPoint(ipAddress, port);
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

            _state = ClientState.start;

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
                ;
                if (data.ContainsKey("MessageID"))
                {
                    if (_processedIds.Contains((ushort)data["MessageID"]))
                    {
                        SendConfirmationPacket((ushort)data["MessageID"]);
                        continue;
                    }
                    //in reply can be changed remote ip so there is sending after
                    else if (messageType != UdpMessageType.REPLY)
                    {
                        _processedIds.Add((ushort)data["MessageID"]);
                        SendConfirmationPacket((ushort)data["MessageID"]);
                    }
                }

                switch (messageType)
                {
                    case UdpMessageType.REPLY:
                        logger.LogInformation("getting reply");
                        ProcessReply(packet, result);
                        break;
                    case UdpMessageType.MSG:
                        logger.LogInformation("getting msg");
                        ProcessMsg(packet);
                        break;
                    case UdpMessageType.PING:
                        break;
                    case UdpMessageType.BYE:
                        //todo wait if not another bye is send
                        EndCommunication(0);
                        break;
                    case UdpMessageType.ERR:
                        Console.WriteLine($"ERROR FROM {data["DisplayName"]}: {data["MessageContents"]}");
                        // todo wait if another bye is sent
                        EndCommunication(1);
                        break;
                    case UdpMessageType.CONFIRM:
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
                        Console.WriteLine("ERROR: recieved invalid packet");
                        _messageBuffer.Add(new UdpMessage()
                        {
                            Type = UdpMessageType.ERR,
                            DisplayName = _user.DisplayName,
                            MessageArgs = ["Invalid message"]
                        });
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
        if (_state == ClientState.start || _state == ClientState.auth)
        {
            Console.WriteLine("ERROR: received msg when not authenticated");
            // todo senderr   
        }

        var message = Deserialize(packet);

        Console.WriteLine($"{message["DisplayName"]}: {message["MessageContents"]}");
    }

    private void ProcessReply(byte[] packet, UdpReceiveResult result)
    {
        var replyMessage = Deserialize(packet);

        //switch to new port
        if (!_usingDinamicPort)
        {
            _remoteEndpoint = result.RemoteEndPoint;
        }

        int resultByte = (int)replyMessage["Result"];
        if (resultByte == 1)
        {
            Console.WriteLine($"Action Success: {replyMessage["MessageContents"]}");
            if (_state == ClientState.auth || _state == ClientState.start)
            {
                _user.setIsAuthenticated(true);
            }

            _state = ClientState.open;
        }
        else
        {
            Console.WriteLine($"Action Failure: {replyMessage["MessageContents"]}");
            if (_state == ClientState.join)
            {
                _state = ClientState.open;
            }
        }

        SendConfirmationPacket((ushort)replyMessage["MessageID"]);

        WaitingForAuthReply = false;
        WaitingForJoinReply = false;
    }

    private void SendConfirmationPacket(ushort confirmingId)
    {
        logger.LogInformation("sending confirmation");
        var confirmMessage = new UdpMessage();
        confirmMessage.DisplayName = _user.DisplayName;
        confirmMessage.Type = UdpMessageType.CONFIRM;


        string error;

        var confirmPacket = confirmMessage.Serialize(out error, confirmingId);
        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine(error);
            Environment.Exit(0);
        }

        _udpClient!.SendAsync(confirmPacket, confirmPacket.Length, _remoteEndpoint);
    }

    private void ProcessUserInput()
    {
        
        while (!_token.IsCancellationRequested)
        {
            logger.LogInformation("status: " + _state.ToString());

            string? input = Console.ReadLine();

            if (input == null)
            {
                logger.LogInformation("ctrl +d pressed");
                _messageBuffer.Add(new UdpMessage()
                {
                    Type = UdpMessageType.BYE,
                    DisplayName = _user.DisplayName
                });
                break; //todo remove - wtf brak was breaking it xd
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();
            switch (command)
            {
                case "/auth":
                    _messageBuffer.Add(new UdpMessage
                    {
                        Type = UdpMessageType.AUTH,
                        MessageArgs = args
                    });
                    break;
                case "/join":
                    _messageBuffer.Add(new UdpMessage
                    {
                        Type = UdpMessageType.JOIN,
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
                        Type = UdpMessageType.MSG,
                        MessageArgs = [input],
                        DisplayName = _user.DisplayName
                    };

                    _messageBuffer.Add(message);
                    break;
            }
        }

        while (true)
        {
            //wait until all things are close
            Thread.Sleep(100);
        }
    }

    public Dictionary<string, object> Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        byte typeByte = reader.ReadByte();
        ushort messageId = 0;
        Dictionary<string, object> result = new()
        {
            ["Type"] = (UdpMessageType)typeByte
        };
        logger.LogInformation($"type: {(UdpMessageType)typeByte}");
        switch ((UdpMessageType)typeByte)
        {
            case UdpMessageType.CONFIRM:
                result["RefMessageID"] = ReadUInt16(reader);
                break;

            case UdpMessageType.REPLY:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["Result"] = (int)reader.ReadByte();
                result["RefMessageID"] = ReadUInt16(reader);
                result["MessageContents"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.AUTH:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["Username"] = ReadZeroTerminatedString(reader);
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                result["Secret"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.JOIN:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["ChannelID"] = ReadZeroTerminatedString(reader);
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.MSG:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                result["MessageContents"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.ERR:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                result["MessageContents"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.BYE:
                messageId = ReadUInt16(reader);
                result["MessageID"] = messageId;
                result["DisplayName"] = ReadZeroTerminatedString(reader);
                break;

            case UdpMessageType.PING:
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
            string error;

            bool allowedToSend = message.Type switch
            {
                UdpMessageType.MSG => !WaitingForAuthReply && !WaitingForJoinReply,
                _ => true
            };

            if (!allowedToSend)
            {
                await Task.Delay(50, _token);
                continue;
            }

            logger.LogInformation("entering sending switch");
            switch (message.Type)
            {
                case UdpMessageType.AUTH:
                    if (_user.isAuthenticated)
                    {
                        Console.WriteLine("ERROR: Already authenticated");
                        break;
                    }

                    if (message.MessageArgs.Length != 3)
                    {
                        Console.WriteLine("ERROR: Usage: /auth <username> <secret> <displayName>");
                        break;
                    }

                    if (!_user.SetUsername(message.MessageArgs[0], out error) ||
                        !_user.SetSecret(message.MessageArgs[1], out error) ||
                        !_user.SetDisplayName(message.MessageArgs[2], out error))
                    {
                        Console.WriteLine(error);
                        break;
                    }

                    _state = ClientState.auth;
                    _messageBuffer.TryGet(out _);
                    WaitingForAuthReply = true;

                    if (!await SendUdpMessage(message))
                    {
                        break;
                    }
                    break;

                case UdpMessageType.MSG: 
                    if (!_user.isAuthenticated)
                    {
                        Console.WriteLine("ERROR: You must be authenticated before sending messages.\n");
                        break;
                    } 
                    if (_state != ClientState.open)
                    {
                        Console.WriteLine("ERROR: cannot send message ");
                        break;
                    }

                    logger.LogInformation("sending msg");
                    await SendUdpMessage(message);
                    break;

                case UdpMessageType.JOIN:
                    if (!_user.isAuthenticated)
                    {
                        Console.WriteLine("ERROR: You must be authenticated before joining a channel.\n");
                        break;
                    }

                    _state = ClientState.join;
                    WaitingForJoinReply = true;

                    logger.LogInformation("sending msg");
                    await SendUdpMessage(message);
                    break;

                case UdpMessageType.BYE:
                    logger.LogInformation("sending bye");
                    await SendUdpMessage(message);

                    EndCommunication(0);
                    break;
                case UdpMessageType.ERR:
                    logger.LogInformation("sending err");
                    await SendUdpMessage(message);
                    
                    EndCommunication(1);
                    break;
            }

            _messageBuffer.TryGet(out _);
        }
    }

    private async Task<bool> SendUdpMessage(UdpMessage message)
    {
        _pendingConfirmationId = _currentMessageId;

        string error;
        var packet = message.Serialize(out error, _currentMessageId);
        if (!string.IsNullOrEmpty(error))
        {
            Console.WriteLine(error);
            _messageBuffer.TryGet(out _);
            return false;
        }

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
            //todo return err
            logger.LogInformation("yes it is from here");
            Console.WriteLine(
                $"ERROR: UDP message {_currentMessageId} failed after {_maxUdpRetransmissions} retransmission attempts");
            _pendingConfirmationId = -1; // Reset pending confirmation
            _currentMessageId++;
            return false;
        }

        _currentMessageId++;
        return true;
    }

    private void EndCommunication(int errorCode)
    {
        _udpClient?.Dispose();
        Environment.Exit(errorCode);
    }
}