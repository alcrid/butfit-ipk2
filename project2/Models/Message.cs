namespace project2.Models;

public abstract class Message
{
    public abstract string? Serialize(out string error); 
    
}