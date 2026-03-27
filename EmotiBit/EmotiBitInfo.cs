// EmotibitInfo.cs
public class EmotibitInfo
{
    public string Ip;
    public bool IsAvailable;
    public long LastSeen; // Using long for Stopwatch milliseconds

    public EmotibitInfo(string ip = "", bool isAvailable = false, long lastSeen = 0)
    {
        this.Ip = ip;
        this.IsAvailable = isAvailable;
        this.LastSeen = lastSeen;
    }
}