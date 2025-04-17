using System.Net;
using project2.Enums;
using project2.Interfaces;

namespace project2.Models;

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

public class TcpMessageClient(IPAddress serverIp, int port, CancellationToken token) : ITcpMessageClient
{
    private TcpClient? _tcpClient;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly CancellationToken _token = token;

    public async void StartCommunication()
    {
        try
        {
            _tcpClient = new TcpClient();
            _tcpClient.Connect(serverIp, port);
            Console.WriteLine("Connected to server.");

            var stream = _tcpClient.GetStream();
            _reader = new StreamReader(stream);
            _writer = new StreamWriter(stream) { AutoFlush = true };

            await Task.Run(ReceiveMessagesAsync, _token);

            while (!_token.IsCancellationRequested)
            {
                var input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Trim().Equals("/quit", StringComparison.CurrentCultureIgnoreCase))
                {
                    Console.WriteLine("Disconnecting...");
                    break;
                }

                var message = new TcpMessage
                {
                    Type = MessageType.MSG,
                    DisplayName = "User",
                    Content = input
                };

                var formatted = message.Serialize();
                await _writer.WriteLineAsync(formatted);
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Session cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
        }
        finally
        {
            _writer?.Close();
            _reader?.Close();
            _tcpClient?.Close();
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
}