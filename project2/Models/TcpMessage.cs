using project2.Enums;
using System.Text.RegularExpressions;

namespace project2.Models;

public class TcpMessage : Message
{
    public TcpMessageType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string[] MessageArgs { get; set; } = Array.Empty<string>();
    public string DisplayName { get; set; } = "Unknown";
    public string Secret { get; set; } = string.Empty;
    private static readonly Regex ChannelIdRegex = new(@"^[a-zA-Z0-9_-]{1,20}$");

    public void SetDisplayName(string displayName)
    {
        DisplayName = displayName;
    }

    public override string Serialize(out string error)
    {
        error = string.Empty;

        switch (Type)
        {
            case TcpMessageType.AUTH:
                if (MessageArgs.Length != 3)
                {
                    error = "ERROR: Usage: /auth <username> <secret> <displayName>";
                    return string.Empty;
                }

                return $"AUTH {MessageArgs[0]} AS {MessageArgs[2]} USING {MessageArgs[1]}\r\n";

            case TcpMessageType.JOIN:
                if (MessageArgs.Length != 1)
                {
                    error = "ERROR: usage: /join <channelid>";
                    return string.Empty;
                }
                else if(!IsChannelIdValid(MessageArgs[0])){
                    error = "ERROR: Invalid channelId provided";
                    return string.Empty;
                }

                return $"JOIN {MessageArgs[0]} AS {DisplayName}\r\n";

            case TcpMessageType.MSG:
                if (Content.Length > 60000)
                {
                    error = "ERROR: Message content too long and was truncated.";
                    Content = Content[..60000];
                }

                return $"MSG FROM {DisplayName} IS {Content}\r\n";
                
            case TcpMessageType.ERR:
                if (string.IsNullOrWhiteSpace(Content))
                {
                    error = "ERROR: Error message content cannot be empty.";
                    return string.Empty;
                }

                return $"ERR FROM {DisplayName} IS {Content}\r\n";

            case TcpMessageType.REPLY:
                return $"REPLY {Content}\r\n";

            case TcpMessageType.BYE:
                return $"BYE FROM {DisplayName}\r\n";

            default:
                error = "ERROR: Unknown message type.";
                return string.Empty;
        }
    }

    private static bool IsChannelIdValid(string input) => ChannelIdRegex.IsMatch(input);
}