# Project 2 
**Author**: xkomanj00 (xkomanj00@vutbr.cz)

**Repository** https://git.fit.vutbr.cz/xkomanj00/project2

## Content

1. [Introduction](#introduction)  
2. [Theory](#theory)  
   2.1 [TCP Message Protocol](#tcp-message-protocol)  
   2.2 [UDP Message Protocol](#udp-message-protocol)  
3. [ABNF (Augmented Backus–Naur Form)](#abnf-augmented-backusnaur-form)  
4. [Code Implementation](#code-implementation)  
   4.1 [Class Diagram](#class-diagram)  
   4.2 [TcmMessageClient.cs](#tcmmessageclientcs)  
   &nbsp;&nbsp;&nbsp;&nbsp;4.2.1 [ProcessUserInput](#processuserinput)  
   &nbsp;&nbsp;&nbsp;&nbsp;4.2.2 [Sending Messages](#sending-messages)  
   4.3 [TcpMessage.cs](#tcpmessagecs)  
   4.4 [UdpMessageClient.cs](#udpmessageclientcs)  
   &nbsp;&nbsp;&nbsp;&nbsp;4.4.1 [SendUdpMessage](#sendudpmessage)  
   &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;4.4.1.1 [Message Sending](#message-sending)  
   &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;4.4.1.2 [Retransmission & Confirmation](#retransmission--confirmation)  
   &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;4.4.1.3 [Deserialize](#deserialize)  
   4.5 [UdpMessage.cs](#udpmessagecs)  
   4.6 [MessageBuffer.cs](#messagebuffercs)  
5. [Tests](#tests)

## Introduction

This project implements a chat client that supports two different network protocols:

- **TCP**: A text-based, connection-oriented protocol that ensures reliable, ordered delivery of messages.
- **UDP**: A binary-based, connectionless protocol focused on speed and efficiency, without guaranteeing reliability or order.

> _Note: This documentation was formatted using a large language model (LLM) with the prompt:_  
> **"Format this text so it is readable and follows proper markdown standards."**

## Theory
### TCP Message Protocol

TCP is a text-based protocol that provides **reliable**, **ordered**, and **error-checked** delivery of data between applications over a network.

It is a **connection-oriented** protocol, meaning a connection must first be established using agreed parameters before data can be exchanged. This is achieved through a **three-way handshake** between the client and server.

TCP ensures that Data is delivered in the correct order and that lost or corrupted packets are retransmitted

TCP prioritizes **reliability** over **speed**.

### UDP Message Protocol

UDP is a **binary-based** transport protocol that, unlike TCP, does **not guarantee reliability, ordering, or error correction**. It operates without establishing a prior communication channel, making it a **connectionless protocol**.

Messages (datagrams) are sent independently, with **no negotiation, session tracking, or built-in retransmission**. UDP does not retain any state about what has been sent or received.

Because of this, **messages may arrive out of order, be duplicated, or be lost entirely**. It is the responsibility of the receiving application to **evaluate incoming messages, detect duplicates, ensure correct ordering, and confirm receipt if needed**.

UDP prioritizes **efficiency and speed**.

# ABNF (Augmented Backus–Naur Form)

ABNF is a **metalanguage** used to describe the grammar of formal languages. It is based on Backus-Naur Form (BNF) but adds features such as **case-insensitive rule names, value ranges, repetition, and alternatives**.

It is widely used in **internet protocol specifications**.

In this project, ABNF was used to formally define the structure of **TCP protocol messages**.

## Code Implementation

### class diagram 
(from rider)

### TcmMessageClient.cs

The client is implemented using `TcpClient` from `System.Net.Sockets`, which manages establishing the connection to the server using the **three way handshake**. Two background tasks handle message sending and receiving, while the main thread processes user input.

```c#
    _tcpClient = new TcpClient(AddressFamily.InterNetwork);
    _tcpClient.Connect(server, port);
    _state = ClientState.start;

    var stream = _tcpClient.GetStream();
    _reader = new StreamReader(stream);
    _writer = new StreamWriter(stream) { AutoFlush = true };

    _ = Task.Run(ReceiveMessagesAsync, _token);
    _ = Task.Run(SendBufferedMessagesAsync, _token);
    ProcessUserInput();
```

#### ProcessUserInput
The method `ProcessUserInput` reads user commands from the console and parses them. Valid commands are converted into `TcpMessage` objects and added to `_messageBuffer`. Invalid commands are rejected with a local error and not added to the buffer.

```c#
    string? input = Console.ReadLine();
    // the input is checkded
    ...
    var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var command = parts[0].ToLower();
    var args = parts.Skip(1).ToArray();
    switch (command)
    {
        case "/auth":
            _messageBuffer.Add(new TcpMessage
            {
                Type = TcpMessageType.AUTH,
                MessageArgs = args
            });
            break;
        // other cases
        ...
    }
```

#### Sending Messages
`SendBufferedMessagesAsync` checks for messages in `_messageBuffer`. If sending is allowed (e.g., not waiting for a reply), it validates and sends the message.

```c#
    // checks if any messages are in the buffer
    if (_messageBuffer.IsEmpty)
    {
        await Task.Delay(100);
        continue;
    }

    var message = _messageBuffer.Peek();
    string error;

    // checks if is allowed to send messages and isn't waiting for a reply
    bool allowedToSend = message.Type switch
    {
        TcpMessageType.MSG => !WaitingForAuthReply && !WaitingForJoinReply,
        _ => true
    };

    if (!allowedToSend)
    {
        await Task.Delay(50, _token);
        continue;
    }

    try
    {
        switch (message.Type)
        {  
            case TcpMessageType.AUTH:
            // Argument validation and message sending
            break;
        // handle other types
        ...
        }
    }
```

### TcpMessage.cs
`TcpMessage` defines the structure and serialization logic of TCP messages stored in the message buffer. When the `Serialize()` function is called, the message type and its arguments are validated. If no errors are found, the message is returned as a properly formatted string.

```c#
public class TcpMessage : Message
{
    public TcpMessageType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public string[] MessageArgs { get; set; } = Array.Empty<string>();
    public string DisplayName { get; set; } = "Unknown";
    public string Secret { get; set; } = string.Empty;
    private static readonly Regex ChannelIdRegex = new(@"^[a-zA-Z0-9_-]{1,20}$");

    public string Serialize(out string error)
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
            // Handle other cases
            ...
        }
    }
    ...
}
```

### UdpMessageClient.cs

The `UdpMessageClient` handles communication over the **UDP protocol** using the `UdpClient` class from `System.Net.Sockets`. Since Udp does not guarantee message delivery, order, or integrity so **retransmissions**, **message confirmation**, and **duplicate detection** has to be implemented in the client.

Exactly like in the tcp client once the client starts, two background tasks are launched:
- `ReceiveMessagesAsync()` listens for incoming UDP packets.
- `SendBufferedMessagesAsync()` handles outgoing messages from a local buffer.

```c#
    _udpClient = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
    _remoteEndpoint = new IPEndPoint(ipAddress, port);

    _token = _cts.Token;

    _ = Task.Run(ReceiveMessagesAsync, _token);
    _ = Task.Run(SendBufferedMessagesAsync, _token);
    ProcessUserInput();
```

#### SendUdpMessage

##### Message sending
Handles the **serialization** and **confirmation of delivery** of UDP messages.  
The message is first serialized into a byte array, and then sent to the server.

```c#
   _pendingConfirmationId = _currentMessageId;

string error;
var packet = message.Serialize(out error, _currentMessageId);
if (!string.IsNullOrEmpty(error))
{
    Console.WriteLine(error);
    _messageBuffer.TryGet(out _);
    return false;
}
```

##### Retransmission & Confirmation
After sending, the client waits for a confirmation (`CONFIRM` packet) from the server.
If no confirmation is received within the timeout period, the message is retransmitted, up to `maxUdpRetransmissions` times.
If all attempts fail, the client terminates with an error.

```c#
int retransmissionCount = 0;
bool confirmed = false;

while (retransmissionCount <= _maxUdpRetransmissions && !confirmed)
{
    await _udpClient!.SendAsync(packet, packet.Length, _remoteEndpoint);

    var confirmationTask = Task.Run(async () =>
    {
        while (_pendingConfirmationId == _currentMessageId)
        {
            await Task.Delay(10); // Prevent CPU hogging
        }
        return _pendingConfirmationId == -1;
    });

    if (await Task.WhenAny(confirmationTask, Task.Delay(_udpConfirmationTimeout)) == confirmationTask)
    {
        confirmed = await confirmationTask;
        break;
    }

    retransmissionCount++;
}

if (!confirmed && retransmissionCount > _maxUdpRetransmissions)
{
    Console.WriteLine(
        $"ERROR: UDP message {_currentMessageId} failed after {_maxUdpRetransmissions} retransmission attempts");
    _pendingConfirmationId = -1;
    _currentMessageId++;
    EndCommunication(1);
    return false;
}

_currentMessageId++;
return true;
```
#### Deserialize

Deserialization of recieved messages is done using a Binary reader that extracts the fields into a null terminated utf8 string.

```c#
public Dictionary<string, object> Deserialize(byte[] data)
{
    using var ms = new MemoryStream(data);
    using var reader = new BinaryReader(ms);

    byte typeByte = reader.ReadByte();
    ushort messageId = 0;
    Dictionary<string, object> result = new()
    {
        ["Type"] = (UdpMessageType)typeByte
    };
    logger.LogInformation($"type: {(UdpMessageType)typeByte}");
    switch ((UdpMessageType)typeByte)
    {
        case UdpMessageType.CONFIRM:
            result["RefMessageID"] = ReadUInt16(reader);
            break;
            // other cases
            ...
    }
}
```

### UdpMessage.cs

`UdpMessage` defines the structure and serialization logic for UDP Messages stored in the message Buffer. 
When the `Serialize()` function is called it validates the input and dispatches to a message-specific creator function based on the message type. Each message is assigned a unique `messageId` and encoded as a byte array in UTF-8 with null-terminated strings.

```c#

    public UdpMessageType Type { get; set; }
    public string DisplayName { get; set; } = "Unknown";
    public byte[] Content { get; set; } = [];
    public string[] MessageArgs { get; set; } = Array.Empty<string>();

    public byte[] Serialize(out string error, ushort messageId)
    {
        error = string.Empty;

        switch (Type)
        {
            case UdpMessageType.AUTH:
                return CreateAuthMessage(messageId, MessageArgs[0], MessageArgs[2], MessageArgs[1]);
            // Handle other cases
            ...
        }
    }
    ...
```

### MessageBuffer.cs
A generic buffer class using `Queue<T>` to store and manage messages.

```c#
public class MessageBuffer<T> : IMessageBuffer<T> where T : Message
{
    private readonly Queue<T> _buffer = new();

    public void Add(T message)
    {
        _buffer.Enqueue(message);
    }

    public bool TryGet(out T? message)
    {
        return _buffer.TryDequeue(out message);
    }

    public T? Peek() => _buffer.TryPeek(out var result) ? result : null;

    public bool IsEmpty => _buffer.Count == 0;
}
```

// co zrobit dolejska server
// TCP UDP

tests - 
TEsting done 

TCP on dolejska server

UDP on dolejska server

TCP on localhost
UDP on localhost
bolest :<

TESTING SEND BYE
This was tested and done for each state except join
./ipk-chat -s -tcp 
(press ctrl+c)
/ serer 
GETTING ERR,BYE
This was tested in all states 
example
./ipk-chat -s -tcp 
ERROR FROM server IS An error has occured
BYE FROM server
Example : 

/TESTING AUTH
/auth a b c
msg1
ERROR: can't send message here 


SERVER:
REPLY IS nOK invaid login

