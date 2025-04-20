using project2.Models;
using Microsoft.Extensions.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        string server = "";
        string protocol = "";
        int port = 4567;
        ushort timeout = 250;
        int retransmissions = 250;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                    Console.WriteLine("""
                        /auth {Username} {Secret} {DisplayName}     Sends AUTH message with the data provided from the command to the server (and correctly handles the Reply message), locally sets the DisplayName value (same as the /rename command)
                        /join {ChannelID}                          Sends JOIN message with channel name from the command to the server (and correctly handles the Reply message)
                        /rename {DisplayName}                      Locally changes the display name of the user to be sent with new messages/selected commands
                        /help                                      Prints out supported local commands with their parameters and a description
                        """);
                    Environment.Exit(0);
                     return;

                case "-t":
                    protocol = args[++i];
                    break;

                case "-s":
                    server = args[++i];
                    break;

                case "-p":
                    if (!int.TryParse(args[++i], out port))
                    {
                        Console.Error.WriteLine("ERROR: Invalid port value.");
                        Environment.Exit(1);
                    }

                    break;

                case "-d":
                    if (!ushort.TryParse(args[++i], out timeout))
                    {
                        Console.Error.WriteLine("ERROR: Invalid UDP timeout value.");
                        Environment.Exit(1);
                    }

                    break;
                case "r":
                    if (!int.TryParse(args[++i], out retransmissions))
                    {
                        Console.Error.WriteLine("ERROR: Invalid retransmittions value.");
                        Environment.Exit(1);
                    }

                    break;
                default:
                    Console.WriteLine("ERROR: Invalid argument passed");
                    Environment.Exit(1);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(protocol))
        {
            Console.Error.WriteLine("ERROR: Server and protocol are required.");
            Environment.Exit(1);
        }

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "[HH:mm:ss] ";
            });
        });

        if (protocol == "tcp")
        {
            var logger = loggerFactory.CreateLogger<TcpMessageClient>();
            TcpMessageClient client = new TcpMessageClient(server, port, logger);
            client.StartCommunication();
        }else if(protocol == "udp"){
            var logger = loggerFactory.CreateLogger<UdpMessageClient>();
            UdpMessageClient client = new UdpMessageClient(server, port, logger, retransmissions, timeout);
            client.StartCommunication();
        }else{
            Console.Error.WriteLine("ERROR: Invalid protocol provided");
            Environment.Exit(1);
        }

        Environment.Exit(0);
    }
}