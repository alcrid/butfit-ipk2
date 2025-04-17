using project2.Utils;

namespace project2.Models;

public class User
{
    public string Username { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public string Secret { get; private set; } = "";

    public bool SetUsername(string username, out string error)
    {
        if (!InputValidator.IsValidUsername(username))
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
        if (!InputValidator.IsValidDisplayName(displayName))
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
}
