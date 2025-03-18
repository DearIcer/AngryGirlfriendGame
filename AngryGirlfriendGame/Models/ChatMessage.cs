namespace AngryGirlfriendGame.Models;

public class ChatMessage
{
    public string Message { get; set; } = string.Empty;
    public int Score { get; set; }
    public int Attempts { get; set; }
}