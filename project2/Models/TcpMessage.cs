using project2.Enums;

namespace project2.Models;

public class TcpMessage : Message
{
    public MessageType Type { get; set; }
    public string DisplayName { get; set; } = "Unknown";
    public string Content { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty; 
    public string Secret { get; set; } = string.Empty; 
    public string ChannelId { get; set; } = string.Empty; 

    public override string Serialize()
    {
        return Type switch
        {
            MessageType.AUTH => $"AUTH {Username} AS {DisplayName} USING {Secret}\r\n",
            MessageType.JOIN => $"JOIN {ChannelId} AS {DisplayName}\r\n",
            MessageType.MSG => $"MSG FROM {DisplayName} IS {Content}\r\n",
            MessageType.ERR => $"ERR FROM {DisplayName} IS {Content}\r\n",
            MessageType.REPLY => $"REPLY {Content}\r\n",
            MessageType.BYE => $"BYE FROM {DisplayName}\r\n",
            _ => throw new InvalidOperationException("Unsupported message type.")
        };
    }
}