using System.Net;
using System.Net.Sockets;
using project2.Enums;
using project2.Interfaces;
using project2.Utils;

namespace project2.Models;

public class TcpMessageClient(IPAddress serverIp, int port, CancellationToken token) : ITcpMessageClient
{
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly CancellationToken _token = token;
    private readonly User _user = new();
    private readonly MessageBuffer<TcpMessage> _messageBuffer = new();
    public ClientState State;

    public async void StartCommunication()
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(serverIp, port);
            State = ClientState.Connected;

            var stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            _ = Task.Run(ReceiveMessagesAsync, _token);

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
            while (!_token.IsCancellationRequested)
            {
                string? line = await _reader!.ReadLineAsync(_token);

                if (line == null)
                {
                    Console.WriteLine("Connection closed by server.");
                    break;
                }

                Console.WriteLine($"Server: {line}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Receive error: {ex.Message}");
        }
    }

    private void ProcessUserInput()
    {
        while (!_token.IsCancellationRequested)
        {
            string? input = Console.ReadLine();

            // Ctrl+D
            if (input == null)
            {
                _messageBuffer.Add(new TcpMessage
                {
                    Type = MessageType.BYE,
                    DisplayName = _user.DisplayName
                });
                ProcessMessage();
                EndCommunication();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

            if (input.StartsWith("/"))
            {
                var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0].ToLower();

                switch (command)
                {
                    case "/auth":
                        if (parts.Length != 4)
                        {
                            Console.WriteLine("ERROR: Usage: /auth <username> <secret> <displayName>");
                            break;
                        }

                        if (!_user.SetUsername(parts[1], out var err) ||
                            !_user.SetSecret(parts[2], out err) ||
                            !_user.SetDisplayName(parts[3], out err))
                        {
                            Console.Write(err);
                            break;
                        }

                        _messageBuffer.Add(new TcpMessage
                        {
                            Type = MessageType.AUTH,
                            Username = _user.Username,
                            Secret = _user.Secret,
                            DisplayName = _user.DisplayName
                        });
                        ProcessMessage();
                        break;

                    case "/join":
                        if (parts.Length != 2)
                        {
                            Console.WriteLine("ERROR: Usage: /join <channelId>");
                            break;
                        }

                        _messageBuffer.Add(new TcpMessage
                        {
                            Type = MessageType.JOIN,
                            ChannelId = parts[1],
                            DisplayName = _user.DisplayName
                        });
                        ProcessMessage();
                        break;

                    case "/rename":
                        if (parts.Length != 2 || !_user.SetDisplayName(parts[1], out var error))
                        {
                            Console.WriteLine(error, " f");
                            break;
                        }

                        Console.WriteLine($"Display name changed to {_user.DisplayName}");
                        break;

                    case "/help":
                        Console.WriteLine("""
                                          /auth {Username} {Secret} {DisplayName}     Sends AUTH message with the data provided from the command to the server (and correctly handles the Reply message), locally sets the DisplayName value (same as the /rename command)
                                          /join {ChannelID}                          Sends JOIN message with channel name from the command to the server (and correctly handles the Reply message)
                                          /rename {DisplayName}                      Locally changes the display name of the user to be sent with new messages/selected commands
                                          /help                                      Prints out supported local commands with their parameters and a description
                                          """);
                        break;

                    case "/quit":
                        _messageBuffer.Add(new TcpMessage
                        {
                            Type = MessageType.BYE,
                            DisplayName = _user.DisplayName
                        });
                        ProcessMessage();
                        EndCommunication();
                        return;

                    default:
                        Console.WriteLine($"ERROR: Unknown command {command}");
                        break;
                }
            }
            else
            {
                // Default: normal chat message
                var message = new TcpMessage
                {
                    Type = MessageType.MSG,
                    DisplayName = _user.DisplayName,
                    Content = input
                };

                _messageBuffer.Add(message);
                ProcessMessage();
            }
        }
    }


    private void ProcessMessage()
    {
        if (_messageBuffer.IsEmpty)
            return;

        if (!_messageBuffer.TryGet(out var message))
            return;

        if (message != null && !message.IsValid(out var error))
        {
            Console.Write(error);
            return;
        }

        switch (message!.Type)
        {
            case MessageType.AUTH:
                if (State != ClientState.Connected)
                {
                    Console.WriteLine("ERROR: Already authenticated.\n");
                    return;
                }

                State = ClientState.WaitingForReply; // wait for REPLY OK/NOK
                break;

            case MessageType.JOIN:
                if (State != ClientState.Authenticated && State != ClientState.InChannel)
                {
                    Console.WriteLine("ERROR: You must be authenticated before joining a channel.\n");
                    return;
                }

                State = ClientState.WaitingForReply;
                break;

            case MessageType.MSG:
                if (State != ClientState.InChannel && State != ClientState.Authenticated)
                {
                    Console.WriteLine("ERROR: You must be authenticated and joined before sending messages.\n");
                    return;
                }

                break;

            case MessageType.BYE:
                State = ClientState.Disconnected;
                break;

            case MessageType.ERR:
            case MessageType.REPLY:
                Console.WriteLine("ERROR: Cannot send server-only message types.\n");
                return;

            default:
                Console.WriteLine("ERROR: Unknown message type.\n");
                return;
        }

        try
        {
            _writer!.WriteLine(message.Serialize());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Failed to send message: {ex.Message}");
        }
    }


    private void SendByeAndExitAsync()
    {
        var byeMessage = new TcpMessage
        {
            Type = MessageType.BYE,
            DisplayName = _user.DisplayName,
        };

        _writer!.WriteLine(byeMessage.Serialize());
        EndCommunication();
    }

    private void EndCommunication()
    {
        _writer?.Close();
        _reader?.Close();
        _tcpClient?.Close();
        _tcpClient?.Dispose();
    }
}