namespace project2.Utils;

using System.Text.RegularExpressions;

public static class InputValidator
{
    private static readonly Regex IdRegex = new("^[a-zA-Z0-9_-]{1,20}$");
    private static readonly Regex SecretRegex = new("^[a-zA-Z0-9_-]{1,128}$");
    private static readonly Regex DisplayNameRegex = new("^[\x21-\x7E]{1,20}$"); 
    private static readonly Regex MessageContentRegex = new(@"^[\x0A\x20-\x7E]{1,60000}$");

    public static bool IsValidUsername(string username) => IdRegex.IsMatch(username);
    public static bool IsValidChannelId(string channelId) => IdRegex.IsMatch(channelId);
    public static bool IsValidSecret(string secret) => SecretRegex.IsMatch(secret);
    public static bool IsValidDisplayName(string name) => DisplayNameRegex.IsMatch(name);
    public static bool IsValidMessageContent(string content) => MessageContentRegex.IsMatch(content);
}
