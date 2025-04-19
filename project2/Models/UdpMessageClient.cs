using System.Net;
using System.Net.Sockets;
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
    private ushort _currentMessageId;
    private IPEndPoint _remoteEndpoint = new(IPAddress.Any, 0);

    public void StartCommunication()
    {
        try
        {
            _token = _cts.Token;

            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                /// TODO add bye udp message
                logger.LogInformation("Ctrl+C detected — BYE message enqueued.");
            };

            _udpClient = new UdpClient(AddressFamily.InterNetwork);
            _udpClient.Connect(server, port);
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
            EndCommunication();
        }
    }
    
    private async Task ReceiveMessagesAsync()
    {
        while (!_token.IsCancellationRequested)
        {
           // TODO impelement :<
        }
    }

      private void ProcessUserInput()
    {
        while (!_token.IsCancellationRequested)
        {
          // OTDO implement :<
        }
    }
     private async Task SendBufferedMessagesAsync()
    {
        while (!_token.IsCancellationRequested)
        {
         // TODO implement 
        }
    }

    private void EndCommunication()
    {
        _udpClient?.Dispose();
        Environment.Exit(0);
    }
}
