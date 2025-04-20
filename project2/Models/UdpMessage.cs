using System.Text;
using project2.Enums;

namespace project2.Models;

public class UdpMessage : Message
{
    public UdpMessageType Type { get; set; }
    public string DisplayName { get; set; } = "Unknown";
    public Byte[] Content { get; set; } = [];
    public string[] MessageArgs { get; set; } = Array.Empty<string>();

    public Byte[] Serialize(ushort messageId)
    {
        switch (Type)
        {
            case UdpMessageType.AUTH:
                return CreateAuthMessage(
                    messageId: messageId,
                    username: MessageArgs[0],
                    secret: MessageArgs[1],
                    displayName: MessageArgs[2]
                );
            case UdpMessageType.CONFIRM:
                return CreateConfirmMessage(
                    refMessageId: messageId
                );
            case UdpMessageType.MSG:
                return CreateMsgMessage(
                    messageId: messageId,
                    displayName: DisplayName,
                    messageContent: MessageArgs[0]
                );
            case UdpMessageType.BYE:
                return CreateByeMessage(
                    messageId: messageId,
                    displayName: DisplayName
                );
            case UdpMessageType.JOIN:
                return CreateJoinMessage(
                    messageId: messageId,
                    channelId: MessageArgs[0],
                    displayName: DisplayName
                );
            case UdpMessageType.ERR:
                return CreateErrMessage(
                    messageId: messageId,
                    errorMessage: MessageArgs[0],
                    displayName: DisplayName
                );
        }

        return [];
    }

    private byte[] CreateErrMessage(ushort messageId, string displayName, string errorMessage)
    {
        var encoding = Encoding.UTF8;

        // Convert strings to null-terminated UTF-8
        byte[] displayNameBytes = encoding.GetBytes(displayName + '\0');
        byte[] errorMessageBytes = encoding.GetBytes(errorMessage + '\0');

        // Total size: 1 byte type + 2 bytes ID + strings
        byte[] message = new byte[1 + 2 + displayNameBytes.Length + errorMessageBytes.Length];

        int offset = 0;

        message[offset++] = 0xFE; // ERR type
        message[offset++] = (byte)(messageId >> 8); // Message ID high byte
        message[offset++] = (byte)(messageId & 0xFF); // Message ID low byte

        offset = CopyToBuffer(message, offset, displayNameBytes);
        CopyToBuffer(message, offset, errorMessageBytes);

        return message;
    }

    private byte[] CreateJoinMessage(ushort messageId, string channelId, string displayName)
    {
        var encoding = Encoding.UTF8;

        // Convert strings to null-terminated UTF-8
        byte[] channelIdBytes = encoding.GetBytes(channelId + '\0');
        byte[] displayNameBytes = encoding.GetBytes(displayName + '\0');

        // Total size: 1 byte type + 2 bytes ID + channelId + displayName
        byte[] message = new byte[1 + 2 + channelIdBytes.Length + displayNameBytes.Length];

        int offset = 0;

        message[offset++] = 0x03; // JOIN type
        message[offset++] = (byte)(messageId >> 8); // Message ID high byte
        message[offset++] = (byte)(messageId & 0xFF); // Message ID low byte

        offset = CopyToBuffer(message, offset, channelIdBytes);
        CopyToBuffer(message, offset, displayNameBytes);

        return message;
    }

    private byte[] CreateAuthMessage(ushort messageId, string username, string displayName, string secret)
    {
        var encoding = Encoding.UTF8;

        // Convert strings and add null-terminators
        byte[] usernameBytes = encoding.GetBytes(username + '\0');
        byte[] displayNameBytes = encoding.GetBytes(displayName + '\0');
        byte[] secretBytes = encoding.GetBytes(secret + '\0');

        // Allocate total buffer
        byte[] message = new byte[1 + 2 + usernameBytes.Length + displayNameBytes.Length + secretBytes.Length];

        int offset = 0;

        message[offset++] = 0x02; // Start byte
        message[offset++] = (byte)(messageId >> 8); // Message ID high byte
        message[offset++] = (byte)(messageId & 0xFF); // Message ID low byte

        // Copy all string fields
        offset = CopyToBuffer(message, offset, usernameBytes);
        offset = CopyToBuffer(message, offset, displayNameBytes);
        CopyToBuffer(message, offset, secretBytes);

        return message;
    }

    private byte[] CreateConfirmMessage(ushort refMessageId)
    {
        byte[] message = new byte[3];

        message[0] = 0x00; // Message type: CONFIRM
        message[1] = (byte)(refMessageId >> 8); // High byte of Ref_MessageID
        message[2] = (byte)(refMessageId & 0xFF); // Low byte of Ref_MessageID

        return message;
    }

    private byte[] CreateByeMessage(ushort messageId, string displayName)
    {
        var encoding = Encoding.UTF8;

        // Convert display name to null-terminated UTF-8
        byte[] displayNameBytes = encoding.GetBytes(displayName + '\0');

        // Total size: 1 byte type + 2 bytes ID + display name
        byte[] message = new byte[1 + 2 + displayNameBytes.Length];

        int offset = 0;

        message[offset++] = 0xFF; // BYE type
        message[offset++] = (byte)(messageId >> 8); // Message ID high byte
        message[offset++] = (byte)(messageId & 0xFF); // Message ID low byte

        CopyToBuffer(message, offset, displayNameBytes);

        return message;
    }

    private byte[] CreateMsgMessage(ushort messageId, string displayName, string messageContent)
    {
        var encoding = Encoding.UTF8;

        // Convert to null-terminated UTF-8 bytes
        byte[] displayNameBytes = encoding.GetBytes(displayName + '\0');
        byte[] messageContentBytes = encoding.GetBytes(messageContent + '\0');

        // Total size: 1 byte type + 2 bytes ID + strings
        byte[] message = new byte[1 + 2 + displayNameBytes.Length + messageContentBytes.Length];

        int offset = 0;

        message[offset++] = 0x04; // MSG type
        message[offset++] = (byte)(messageId >> 8); // Message ID high byte
        message[offset++] = (byte)(messageId & 0xFF); // Message ID low byte

        offset = CopyToBuffer(message, offset, displayNameBytes);
        CopyToBuffer(message, offset, messageContentBytes);

        return message;
    }

    private int CopyToBuffer(byte[] buffer, int offset, byte[] data)
    {
        Array.Copy(data, 0, buffer, offset, data.Length);
        return offset + data.Length;
    }
}