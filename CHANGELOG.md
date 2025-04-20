## Implemented Functionality

- Support for the IPK25-CHAT protocol over both **TCP** and **UDP**
- Client-side state machine fully implemented according to the assignment specification
- Parsing and handling of commands: `/auth`, `/join`, `/rename`, and regular messages
- Input validation for all outgoing messages and format checking for received ones
- Asynchronous message receiving to avoid blocking the main thread (TCP)
- UDP: retransmission and message confirmation using timeouts and `MessageID`
- Automatic handling of termination signals (`CTRL+C`, `CTRL+D`)
- Message type handling: AUTH, JOIN, MSG, ERR, REPLY, BYE, CONFIRM
- Reuse of argument parser from the first IPK project

## Limitations

- Due to how messages are processed, there may be a short delay between sending and actual processing
- No known functional limitations beyond that
