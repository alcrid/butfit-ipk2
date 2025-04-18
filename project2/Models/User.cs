using System.Text.RegularExpressions;
using project2.Utils;

namespace project2.Models;

public class User
{
    public string Username { get; private set; } = "";
    public string DisplayName { get; private set; } = "Unknown";
    public string Secret { get; private set; } = "";
    public bool isAuthenticated {get; private set;} = false;

    private static readonly Regex UsernameRegex = new(@"^[a-zA-Z0-9_-]{1,20}$");
    private static readonly Regex DisplayNameRegex = new(@"^[\x21-\x7E]{1,20}$");
    private static readonly Regex SecretRegex = new(@"^[a-zA-Z0-9_-]{1,128}$");

    public void setIsAuthenticated(bool isAuth){
        isAuthenticated = isAuth;
    }

    public bool SetUsername(string username, out string error)
    {
        if (string.IsNullOrWhiteSpace(username) || !IsValidUsername(username))
        {
            error = "ERROR: Invalid username.\n";
            return false;
        }

        Username = username;
        error = "";
        return true;
    }

    public bool SetDisplayName(string displayName, out string error)
    {
        if (string.IsNullOrWhiteSpace(displayName) || !IsValidDisplayName(displayName))
        {
            error = "ERROR: Invalid display name.\n";
            return false;
        }

        DisplayName = displayName;
        error = "";
        return true;
    }

    public bool SetSecret(string secret, out string error)
    {
        if (!InputValidator.IsValidSecret(secret))
        {
            error = "ERROR: Invalid secret.\n";
            return false;
        }

        Secret = secret;
        error = "";
        return true;
    }

    private static bool IsValidUsername(string input) => UsernameRegex.IsMatch(input);

    private static bool IsValidDisplayName(string input) => DisplayNameRegex.IsMatch(input);

    private static bool IsValidSecret(string input) => SecretRegex.IsMatch(input);
}
