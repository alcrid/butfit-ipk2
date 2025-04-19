using project2.Models;
using Microsoft.Extensions.Logging;

internal static class Program
{
    static void Main(string[] args)
    {
        string server = "";
        string protocol = "";
        int port = 4567, timeout = 250, retransmissions = 3;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h":
                    Console.WriteLine(
                        "Usage: ./ipk-l4-scan [-i interface] [-t ports] [-u ports] [-w timeout] hostname/ip");
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
                        Console.Error.WriteLine("Invalid port value.");
                        Environment.Exit(1);
                    }

                    break;

                case "-d":
                    if (!int.TryParse(args[++i], out timeout))
                    {
                        Console.Error.WriteLine("Invalid UDP timeout value.");
                        Environment.Exit(1);
                    }

                    break;
                case "r":
                    if (!int.TryParse(args[++i], out retransmissions))
                    {
                        Console.Error.WriteLine("Invalid retransmittions value.");
                        Environment.Exit(1);
                    }

                    break;
                default:
                    Console.WriteLine("Something bad happened D:");
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
            UdpMessageClient client = new UdpMessageClient(server, port, logger);
            client.StartCommunication();
        }else{
            Console.Error.WriteLine("ERROR: Invalid protocol provided");
            Environment.Exit(1);
        }

        Environment.Exit(0);
    }
}