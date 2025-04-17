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
    public bool IsValid(out string errorMessage)
    {
        switch (Type)
        {
            case MessageType.AUTH:
                if (!IsValidId(Username))
                {
                    errorMessage = "ERROR: Invalid username.\n";
                    return false;
                }
                if (!IsValidDisplayName(DisplayName))
                {
                    errorMessage = "ERROR: Invalid display name.\n";
                    return false;
                }
                if (!IsValidSecret(Secret))
                {
                    errorMessage = "ERROR: Invalid secret.\n";
                    return false;
                }
                break;

            case MessageType.JOIN:
                if (!IsValidId(ChannelId))
                {
                    errorMessage = "ERROR: Invalid channel ID.\n";
                    return false;
                }
                if (!IsValidDisplayName(DisplayName))
                {
                    errorMessage = "ERROR: Invalid display name.\n";
                    return false;
                }
                break;

            case MessageType.MSG:
                if (!IsValidDisplayName(DisplayName))
                {
                    errorMessage = "ERROR: Invalid display name.\n";
                    return false;
                }
                if (Content.Length > 60000)
                {
                    errorMessage = "ERROR: Message content too long and was truncated.\n";
                    Content = Content[..60000];   
                    return false;
                }
                break;

            case MessageType.BYE:
            case MessageType.ERR:
                if (!IsValidDisplayName(DisplayName))
                {
                    errorMessage = "ERROR: Invalid display name.\n";
                    return false;
                }
                if (Type == MessageType.ERR && string.IsNullOrWhiteSpace(Content))
                {
                    errorMessage = "ERROR: Error message content cannot be empty.\n";
                    return false;
                }
                break;
        }

        errorMessage = "";
        return true;
    }

    // Validation helpers
    private static bool IsValidId(string input) =>
        input.Length is > 0 and <= 20 &&
        input.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

    private static bool IsValidDisplayName(string input) =>
        input.Length is > 0 and <= 20 &&
        input.All(c => c >= 0x21 && c <= 0x7E);

    private static bool IsValidSecret(string input) =>
        input.Length is > 0 and <= 128 &&
        input.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

}