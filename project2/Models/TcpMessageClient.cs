using System.Net;
using System.Net.Sockets;
using project2.Enums;
using project2.Utils;
using Microsoft.Extensions.Logging;

namespace project2.Models;

public class TcpMessageClient(string server, int port, ILogger<TcpMessageClient> logger)
{
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly CancellationTokenSource _cts = new();
    private CancellationToken _token;
    private readonly User _user = new();
    private readonly MessageBuffer<TcpMessage> _messageBuffer = new();
    private ClientState _state;
    private bool WaitingForAuthReply;
    private bool WaitingForJoinReply;

    public void StartCommunication()
    {
        try
        {
            _token = _cts.Token;

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                _messageBuffer.Add(new TcpMessage
                {
                    Type = MessageType.BYE,
                    DisplayName = _user.DisplayName
                });

                logger.LogInformation("Ctrl+C detected — BYE message enqueued.");
            };

            _tcpClient = new TcpClient(AddressFamily.InterNetwork);
            _tcpClient.Connect(server, port);
            _state = ClientState.start;

            var stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

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
            EndCommunication();
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            var buffer = string.Empty;
            while (!_token.IsCancellationRequested)
            {
                char[] chunk = new char[1024];
                int read = await _reader!.ReadAsync(chunk, 0, chunk.Length);

                if (read == 0)
                {
                    EndCommunication();
                    break;
                }

                buffer += new string(chunk, 0, read);

                string[] messages = buffer.Split("\r\n");

                for (int i = 0; i < messages.Length - 1; i++)
                {
                    string line = messages[i].Trim();
                    logger.LogInformation("Received: {line}", line);

                    if (line.StartsWith("ERR FROM ", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("BYE FROM ", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine(line);
                        logger.LogWarning("Received terminal message from server. Closing...");
                        EndCommunication();
                        return;
                    }

                    switch (_state)
                    {
                        case ClientState.start:
                            break;

                        case ClientState.auth:
                            if (line.StartsWith("REPLY OK IS ", StringComparison.OrdinalIgnoreCase) &&
                                WaitingForAuthReply)
                            {
                                _state = ClientState.open;
                                _user.setIsAuthenticated(true);
                                WaitingForAuthReply = false;
                            }
                            else if (line.StartsWith("REPLY NOK IS ", StringComparison.OrdinalIgnoreCase) &&
                                     WaitingForAuthReply)
                            {
                                _state = ClientState.auth;
                                WaitingForAuthReply = false;
                            }
                            else
                            {
                                _state = ClientState.end;
                                SendErrAndEndCommunication("Invalid reponse from server");
                            }

                            break;

                        case ClientState.open:
                            if (line.StartsWith("REPLY IS OK ", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("REPLY IS NOK ", StringComparison.OrdinalIgnoreCase))
                            {
                                _state = ClientState.end;
                                SendErrAndEndCommunication("Unexpected REPLY received in OPEN state.");
                            }
                            else
                            {
                                Console.WriteLine(line);
                            }

                            break;
                        case ClientState.join:
                            if ((line.StartsWith("REPLY OK IS ", StringComparison.OrdinalIgnoreCase) ||
                                 line.StartsWith("REPLY NOK IS ", StringComparison.OrdinalIgnoreCase)) &&
                                WaitingForJoinReply)
                            {
                                _state = ClientState.open;
                                WaitingForJoinReply = false;
                            }
                            else
                            {
                                Console.WriteLine(line);
                            }

                            break;

                        case ClientState.end:
                            return;
                    }
                }

                // the last message becomes the buffer
                buffer = messages[messages.Length - 1];
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Receive error.");
        }
    }

    private void ProcessUserInput()
    {
        while (!_token.IsCancellationRequested)
        {
            string? input = Console.ReadLine();

            if (input == null)
            {
                _messageBuffer.Add(new TcpMessage
                {
                    Type = MessageType.BYE,
                    DisplayName = _user.DisplayName
                });
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();
            switch (command)
            {
                case "/auth":
                    _messageBuffer.Add(new TcpMessage
                    {
                        Type = MessageType.AUTH,
                        MessageArgs = args
                    });
                    break;

                case "/join":
                    _messageBuffer.Add(new TcpMessage
                    {
                        Type = MessageType.JOIN,
                        MessageArgs = args
                    });
                    break;

                case "/rename":
                    _messageBuffer.Add(new TcpMessage
                    {
                        Type = MessageType.RENAME,
                        MessageArgs = args
                    });

                    logger.LogInformation($"Display name changed to {_user.DisplayName}");
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

                    var message = new TcpMessage
                    {
                        Type = MessageType.MSG,
                        Content = input
                    };

                    _messageBuffer.Add(message);
                    break;
            }
        }
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
            if (message == null)
            {
                await Task.Delay(100);
                continue;
            }

            string error;
            bool allowedToSend = message.Type switch
            {
                MessageType.AUTH => _state == ClientState.auth || _state == ClientState.start,
                MessageType.JOIN => _state == ClientState.open,
                MessageType.MSG => !WaitingForAuthReply && !WaitingForJoinReply,
                _ => true
            };

            if (!allowedToSend)
            {
                await Task.Delay(100, _token);
                continue;
            }

            try
            {
                switch (message.Type)
                {
                    case MessageType.BYE:
                        message.SetDisplayName(_user.DisplayName);
                        var byeSerialized = message.Serialize(out error);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine(error);
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        await _writer!.WriteAsync(byeSerialized);
                        logger.LogInformation("Sent BYE to server.");
                        _messageBuffer.TryGet(out _);
                        EndCommunication();
                        return;

                    case MessageType.JOIN:
                        if (!_user.isAuthenticated)
                        {
                            Console.WriteLine("ERROR: You must be authenticated before joining a channel.\n");
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        message.SetDisplayName(_user.DisplayName);
                        var joinSerialized = message.Serialize(out error);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine(error);
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        await _writer!.WriteAsync(joinSerialized);
                        _messageBuffer.TryGet(out _);
                        _state = ClientState.join;
                        WaitingForJoinReply = true;
                        break;
                    case MessageType.AUTH:
                        if (_user.isAuthenticated)
                        {
                            Console.WriteLine("ERROR: Already authenticated.");
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        if (message.MessageArgs.Length != 3)
                        {
                            Console.WriteLine("ERROR: Usage: /auth <username> <secret> <displayName>");
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        if (!_user.SetUsername(message.MessageArgs[0], out error) ||
                            !_user.SetSecret(message.MessageArgs[1], out error) ||
                            !_user.SetDisplayName(message.MessageArgs[2], out error))
                        {
                            Console.WriteLine(error);
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        var authSerialized = message.Serialize(out error);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine(error);
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        await _writer!.WriteAsync(authSerialized);
                        _messageBuffer.TryGet(out _);
                        _state = ClientState.auth;
                        WaitingForAuthReply = true;
                        break;

                    default:
                        if (!_user.isAuthenticated)
                        {
                            Console.WriteLine("ERROR: You must be authenticated before sending messages.\n");
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        message.SetDisplayName(_user.DisplayName);
                        var msgSerialized = message.Serialize(out error);
                        if (!string.IsNullOrEmpty(error))
                        {
                            Console.WriteLine(error);
                            _messageBuffer.TryGet(out _);
                            break;
                        }

                        await _writer!.WriteAsync(msgSerialized);
                    _messageBuffer.TryGet(out _);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send message.");
            }
        }
    }

    private void SendByeAndEndCommunication()
    {
        try
        {
            var byeMessage = new TcpMessage
            {
                Type = MessageType.BYE,
                DisplayName = _user.DisplayName
            };

            var serializedMsg = byeMessage.Serialize(out var error);

            if (!string.IsNullOrEmpty(error))
            {
                logger.LogWarning("Failed to serialize BYE message: {Error}", error);
            }
            else
            {
                _writer?.WriteLine(serializedMsg);
                logger.LogInformation("Sent BYE to server.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send BYE message.");
        }
        finally
        {
            _cts.Cancel();
            EndCommunication();
        }
    }

    private void SendErrAndEndCommunication(string reason)
    {
        try
        {
            var errMessage = new TcpMessage
            {
                Type = MessageType.ERR,
                DisplayName = _user.DisplayName,
                Content = reason
            };

            var serializedMsg = errMessage.Serialize(out var error);
            if (!string.IsNullOrEmpty(error))
            {
                logger.LogInformation("Failed to serialize ERR message: {Error}", error);
            }
            else
            {
                _writer?.WriteLine(serializedMsg);
                logger.LogInformation("Invalid reply from server");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send ERR message.");
        }
        finally
        {
            _cts.Cancel();
            EndCommunication();
        }
    }

    private void EndCommunication()
    {
        _writer?.Close();
        _reader?.Close();
        _tcpClient?.Close();
        _tcpClient?.Dispose();
        Environment.Exit(0);
    }
}