using System.Text;
using System.Net;
using System.Text.RegularExpressions;
using project2.Enums;

namespace project2.Models;

public class UdpMessage : Message
{
    // TODO write the same as TcpMessage
    public override string Serialize(out string error)
    {
        // NOT IMPELEMENTED
        error = "";
        return ""; 
    }
}
