using System.Net.Sockets;
using project2.Enums;
using project2.Utils;
using project2.Interfaces;
using Microsoft.Extensions.Logging;

namespace project2.Models;

public class TcpMessageClient(string server, int port, ILogger<TcpMessageClient> logger) : IMessageClient
{
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly CancellationTokenSource _cts = new();
    private CancellationToken _token;
    private readonly User _user = new();
    private readonly MessageBuffer<TcpMessage> _messageBuffer = new();
    private ClientState _state;
    private bool _waitingForAuthReply;
    private bool _waitingForJoinReply;

    public void StartCommunication()
    {
        try
        {
            _token = _cts.Token;
            // takes care of ctrl+c
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _messageBuffer.Add(new TcpMessage
                {
                    Type = TcpMessageType.Bye,
                    DisplayName = _user.DisplayName
                });

                logger.LogInformation("Ctrl+C detected — BYE message enqueued.");
            };

            _tcpClient = new TcpClient(AddressFamily.InterNetwork);
            _tcpClient.Connect(server, port);
            _state = ClientState.Start;

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
            EndCommunication(0);
        }
    }

    private async Task ReceiveMessagesAsync()
    {
        try
        {
            var buffer = string.Empty;
            while (!_token.IsCancellationRequested)
            {
                // message part retrieved
                char[] chunk = new char[1024];
                int read = await _reader!.ReadAsync(chunk, 0, chunk.Length);

                if (read == 0)
                {
                    EndCommunication(0);
                    break;
                }
                
                // Chunk gets added to message
                buffer += new string(chunk, 0, read);
                string[] messages = buffer.Split("\r\n");

                // Each message is then processed
                for (int i = 0; i < messages.Length - 1; i++)
                {
                    string line = messages[i].Trim();
                    var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (words.Length >= 4 && words[0].Equals("ERR", StringComparison.OrdinalIgnoreCase) &&
                        words[1].Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                        words[3].Equals("IS", StringComparison.OrdinalIgnoreCase))
                    {
                        string displayName = words[2];
                        string errorContent = string.Join(' ', words.Skip(4));
                        Console.WriteLine($"ERROR FROM {displayName}: {errorContent}");
                        EndCommunication(1);
                        return;
                    }
                    if (words.Length >= 3 && words[0].Equals("BYE", StringComparison.OrdinalIgnoreCase) &&
                             words[1].Equals("FROM", StringComparison.OrdinalIgnoreCase))
                    {
                        string displayName = words[2];
                        Console.WriteLine($"BYE FROM {displayName}");
                        EndCommunication(0);
                        return;
                    }

                    switch (_state)
                    {
                        case ClientState.Start:
                            // no message except BYE or ERR can be retrieved in this state
                            Console.WriteLine("ERROR: Received invalid response from server");
                            SendErrAndEndCommunication("Invalid response from server");
                            break;

                        case ClientState.Auth:
                            // REPLY
                            if (line.StartsWith("REPLY OK IS ", StringComparison.OrdinalIgnoreCase) &&
                                _waitingForAuthReply)
                            {
                                // Auth is succesfull
                                string message = line["REPLY OK IS ".Length..].Trim();
                                Console.WriteLine($"Action Success: {message}");
                                _state = ClientState.Open;
                                _user.SetIsAuthenticated(true);
                                _waitingForAuthReply = false;
                            }
                            // !RELY
                            else if (line.StartsWith("REPLY NOK IS ", StringComparison.OrdinalIgnoreCase) &&
                                     _waitingForAuthReply)
                            {
                                string message = line["REPLY NOK IS ".Length..].Trim();
                                Console.WriteLine($"Action Failure: {message}");
                                _state = ClientState.Auth;
                                _waitingForAuthReply = false;
                            }
                            else
                            {
                                // invalid message got in this state 
                                Console.WriteLine("ERROR: Received invalid response from server");
                                SendErrAndEndCommunication("Invalid response from server");
                            }

                            break;

                        case ClientState.Open:
                            // !REPLY
                            if (line.StartsWith("REPLY IS OK ", StringComparison.OrdinalIgnoreCase) ||
                                line.StartsWith("REPLY IS NOK ", StringComparison.OrdinalIgnoreCase))
                            {
                                Console.WriteLine("ERROR: Received invalid response from server");
                                SendErrAndEndCommunication("Received invalid message REPLY IS OK or NOK from server");
                            }
                            // MSG
                            else if (words.Length >= 4 &&
                                     words[0].Equals("MSG", StringComparison.OrdinalIgnoreCase) &&
                                     words[1].Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                                     words[3].Equals("IS", StringComparison.OrdinalIgnoreCase))
                            {
                                string displayName = words[2];
                                string content = string.Join(' ', words.Skip(4));
                                Console.WriteLine($"{displayName}: {content}");
                            }
                            else
                            {
                                Console.WriteLine("ERROR: Malformed message received from server");
                                SendErrAndEndCommunication("Malformed message received from server.");
                            }

                            break;

                        case ClientState.Join:
                            // REPLY
                            if (line.StartsWith("REPLY OK IS ", StringComparison.OrdinalIgnoreCase) &&
                                _waitingForJoinReply)
                            {
                                string message = line["REPLY OK IS ".Length..].Trim();
                                Console.WriteLine($"Action Success: {message}");
                                _state = ClientState.Open;
                                _waitingForJoinReply = false;
                            }
                            // !REPLY
                            else if (line.StartsWith("REPLY NOK IS ", StringComparison.OrdinalIgnoreCase) &&
                                     _waitingForJoinReply)
                            {
                                string message = line["REPLY NOK IS ".Length..].Trim();
                                Console.WriteLine($"Action Failure: {message}");
                                _state = ClientState.Open;
                                _waitingForJoinReply = false;
                            }
                            // MSG
                            else if (words.Length >= 4 &&
                                     words[0].Equals("MSG", StringComparison.OrdinalIgnoreCase) &&
                                     words[1].Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                                     words[3].Equals("IS", StringComparison.OrdinalIgnoreCase))
                            {
                                string displayName = words[2];
                                string content = string.Join(' ', words.Skip(4));
                                Console.WriteLine($"{displayName}: {content}");
                            }
                            else
                            {
                                Console.WriteLine("ERROR: Malformed message received from server");
                                SendErrAndEndCommunication("ERROR: Malformed message received from server.");
                            }

                            break;

                        case ClientState.End:
                            return;
                    }
                }

                // the last message becomes the buffer
                buffer = messages[^1];
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: Received error {ex}");
        }
    }

    private void ProcessUserInput()
    {
        while (!_token.IsCancellationRequested)
        {
            string? input = Console.ReadLine();

            // ctrl+d passed
            if (input == null)
            {
                SendByeAndEndCommunication();
                break;
            }
            
            // if empty input ignores it
            if (string.IsNullOrWhiteSpace(input))
                continue;

            // message gets split into command and args that are added to the message
            var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();
            switch (command)
            {
                case "/auth":
                    _messageBuffer.Add(new TcpMessage
                    {
                        Type = TcpMessageType.Auth,
                        MessageArgs = args
                    });
                    break;

                case "/join":
                    _messageBuffer.Add(new TcpMessage
                    {
                        Type = TcpMessageType.Join,
                        MessageArgs = args
                    });
                    break;

                case "/rename":
                    _user.SetDisplayName(args[0], out var error);
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

                    var message = new TcpMessage
                    {
                        Type = TcpMessageType.Msg,
                        Content = input,
                        DisplayName = _user.DisplayName
                    };

                    _messageBuffer.Add(message);
                    break;
            }
        }
    }

    // sends the messages stored in the buffer
    private async Task SendBufferedMessagesAsync()
    {
        while (!_token.IsCancellationRequested)
        {
            // checks if any message are in the buffer
            if (_messageBuffer.IsEmpty)
            {
                await Task.Delay(100, _token);
                continue;
            }

            var message = _messageBuffer.Peek();

            // checks if is allowed to send messages and isn't waiting for a reply
            bool allowedToSend = message != null && message.Type switch
            {
                TcpMessageType.Msg => !_waitingForAuthReply && !_waitingForJoinReply,
                _ => true
            };

            if (!allowedToSend)
            {
                await Task.Delay(50, _token);
                continue;
            }

            try
            {
                if (message != null)
                {
                    string error;
                    switch (message.Type)
                    {
                        case TcpMessageType.Auth:
                            // Checks if arguments passed to message are correct
                            if (_user.IsAuthenticated)
                            {
                                Console.WriteLine("ERROR: Already authenticated");
                                break;
                            }
                            else if (message.MessageArgs.Length != 3)
                            {
                                Console.WriteLine("ERROR: Usage: /auth <username> <secret> <displayName>");
                                break;
                            }
                            else if (!_user.SetUsername(message.MessageArgs[0], out error) ||
                                     !_user.SetSecret(message.MessageArgs[1], out error) ||
                                     !_user.SetDisplayName(message.MessageArgs[2], out error))
                            {
                                Console.WriteLine(error);
                                break;
                            }

                            var authSerialized = message.Serialize(out error);
                            if (!string.IsNullOrEmpty(error))
                            {
                                Console.WriteLine(error);
                                break;
                            }

                            await _writer!.WriteAsync(authSerialized);
                            _state = ClientState.Auth;
                            _waitingForAuthReply = true;
                            break;

                        case TcpMessageType.Msg:
                            if (!_user.IsAuthenticated)
                            {
                                Console.WriteLine("ERROR: You must be authenticated before sending messages.");
                                break;
                            }
                            else if (_state != ClientState.Open)
                            {
                                Console.WriteLine("ERROR: cannot send message.");
                                break;
                            }

                            var msgSerialized = message.Serialize(out error);
                            if (!string.IsNullOrEmpty(error))
                            {
                                Console.WriteLine(error);
                                break;
                            }

                            await _writer!.WriteAsync(msgSerialized);
                            break;

                        case TcpMessageType.Join:
                            if (!_user.IsAuthenticated)
                            {
                                Console.WriteLine("ERROR: You must be authenticated before joining a channel.");
                                break;
                            }

                            message.SetDisplayName(_user.DisplayName);
                            var joinSerialized = message.Serialize(out error);
                            if (!string.IsNullOrEmpty(error))
                            {
                                Console.WriteLine(error);
                                break;
                            }

                            await _writer!.WriteAsync(joinSerialized);
                            _state = ClientState.Join;
                            _waitingForJoinReply = true;
                            break;

                        case TcpMessageType.Bye:
                            var byeSerialized = message.Serialize(out error);
                            if (!string.IsNullOrEmpty(error))
                            {
                                Console.WriteLine(error);
                                break;
                            }

                            await _writer!.WriteAsync(byeSerialized);
                            EndCommunication(0);
                            return;
                    }
                }

                _messageBuffer.TryGet(out _);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send message.");
            }
        }
    }

    // used to bypass the buffer and immediatelly send ERR and finish
    private void SendErrAndEndCommunication(string reason)
    {
        try
        {
            var errMessage = new TcpMessage
            {
                Type = TcpMessageType.Err,
                DisplayName = _user.DisplayName,
                Content = reason
            };

            var serializedMsg = errMessage.Serialize(out var error);
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine(error);
            }

            _writer?.Write(serializedMsg);
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

    // Bypass message bugger to end Bye and end with exit code 0
    private void SendByeAndEndCommunication()
    {
        try
        {
            var errMessage = new TcpMessage
            {
                Type = TcpMessageType.Bye,
                DisplayName = _user.DisplayName,
            };

            var serializedMsg = errMessage.Serialize(out var error);
            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine(error);
            }

            _writer?.Write(serializedMsg);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send ERR message.");
        }
        finally
        {
            EndCommunication(0);
        }
    }

    private void EndCommunication(int errorCode)
    {
        _cts.Cancel();
        _writer?.Close();
        _reader?.Close();
        _tcpClient?.Close();
        _tcpClient?.Dispose();
        Environment.Exit(errorCode);
    }
}