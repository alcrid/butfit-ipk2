// Updated TcpMessageClient.cs using simplified FSM states
using System.Net;
using System.Net.Sockets;
using project2.Enums;
using project2.Interfaces;
using project2.Utils;
using Microsoft.Extensions.Logging;

namespace project2.Models;

public class TcpMessageClient(IPAddress serverIp, int port, CancellationToken token, ILogger<TcpMessageClient> logger) : ITcpMessageClient
{
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly CancellationToken _token = token;
    private readonly User _user = new();
    private readonly MessageBuffer<TcpMessage> _messageBuffer = new();
    public ClientState State;
    private readonly ILogger<TcpMessageClient> _logger = logger;
    private bool WaitingForAuthReply = false;
    private bool WaitingForJoinReply = false;

    public async void StartCommunication()
    {
        try
        {
            _logger.LogInformation("test test");
            _tcpClient = new TcpClient();
            _tcpClient.Connect(serverIp, port);
            State = ClientState.start;

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
            while (!_token.IsCancellationRequested)
            {
                string? line = await _reader!.ReadLineAsync(_token);

                if (line == null)
                {
                    Console.WriteLine("Connection closed by server.");
                    break;
                }

                _logger.LogDebug("Received: {line}", line);

                if (line.StartsWith("ERR FROM ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("BYE FROM ", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine(line);
                    _logger.LogWarning("Received terminal message from server. Closing...");
                    EndCommunication();
                    return; 
                }
                   switch (State)
                {
                    case ClientState.start:
                        // can recieve anyting in this state
                        // if (line.StartsWith("REPLY OK IS ", StringComparison.OrdinalIgnoreCase) && WaitingForAuthReply)
                        // {
                        //     Console.WriteLine($"Action Success: {line.Substring(13)}");
                        //     State = ClientState.auth;
                        //     WaitingForAuthReply = false;
                        // }
                        // else if (line.StartsWith("REPLY NOK IS ", StringComparison.OrdinalIgnoreCase) && WaitingForAuthReply)
                        // {
                        //     Console.WriteLine($"Action Failure: {line.Substring(14)}");
                        //     State = ClientState.start;
                        //     WaitingForAuthReply = false;
                        // }
                        break;

                    case ClientState.auth:
                        if (line.StartsWith("REPLY OK IS ", StringComparison.OrdinalIgnoreCase) && WaitingForJoinReply)
                        {
                            Console.WriteLine($"Action Success: {line.Substring(13)}");
                            State = ClientState.open;
                            WaitingForJoinReply = false;
                        }
                        else if (line.StartsWith("REPLY NOK IS ", StringComparison.OrdinalIgnoreCase) && WaitingForJoinReply)
                        {
                            Console.WriteLine($"Action Failure: {line.Substring(14)}");
                            State = ClientState.auth;
                            WaitingForJoinReply = false;
                        }
                        break;

                    case ClientState.open:
                        Console.WriteLine(line);
                        break;
                    case ClientState.join:
                        break;

                    case ClientState.end:
                        return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Receive error.");
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
                EndCommunication();
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
                continue;

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
                        WaitingForAuthReply = true;

                        break;

                    case "/join":
                        if (State != ClientState.auth && State != ClientState.join)
                        {
                            Console.WriteLine("ERROR: You must be authenticated before joining a channel.\n");
                            break;
                        }

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
                        WaitingForJoinReply = true;
                        break;

                    case "/rename":
                        var error = string.Empty;
                        if (parts.Length != 2 || !_user.SetDisplayName(parts[1], out error))
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
                        EndCommunication();
                        return;

                    default:
                        if ((State != ClientState.auth && State != ClientState.join) && !WaitingForAuthReply && !WaitingForJoinReply)
                        {
                            Console.WriteLine("ERROR: You must be authenticated and joined before sending messages.\n");
                            break;
                        }

                        var message = new TcpMessage
                        {
                            Type = MessageType.MSG,
                            DisplayName = _user.DisplayName,
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
                await Task.Delay(50);
                continue;
            }

            if (!message.IsValid(out var error))
            {
                Console.Write(error);
                _messageBuffer.TryGet(out _);
                continue;
            }

            bool allowedToSend = false;
            if (message.Type == MessageType.AUTH && State == ClientState.start)
                allowedToSend = true;
            else if (message.Type == MessageType.JOIN && (State == ClientState.auth || State == ClientState.join))
                allowedToSend = true;
            else if (message.Type == MessageType.MSG && (State == ClientState.auth || State == ClientState.join))
                allowedToSend = true;
            else if (message.Type == MessageType.BYE)
                allowedToSend = true;

            if (!allowedToSend)
            {
                await Task.Delay(100);
                continue;
            }
            try
            {
                await _writer!.WriteLineAsync(message.Serialize());
                _logger.LogDebug("Sent message of type: {type}", message.Type);

                _messageBuffer.TryGet(out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send message.");
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

            _writer?.WriteLine(byeMessage.Serialize());
            _logger.LogInformation("Sent BYE to server.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send BYE message.");
        }
        finally
        {
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

            _writer?.WriteLine(errMessage.Serialize());
            _logger.LogWarning("Sent ERR to server with reason: {Reason}", reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send ERR message.");
        }
        finally
        {
            EndCommunication();
        }
    }

    private void EndCommunication()
    {
        _writer?.Close();
        _reader?.Close();
        _tcpClient?.Close();
        _tcpClient?.Dispose();
    }
}
