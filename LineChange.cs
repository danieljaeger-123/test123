using LibGit2Sharp;

public struct LineChange
{
    public Line Line { get; set; }
    public Change Change { get; set; }
}

public enum Change
{
    ADDED, DELETED, NONE
}

public static class Extensions
{
    public static string GetIdentifer(this Change change)
    {
        switch(change)
        {
            case Change.ADDED:
                return "[+]";

            case Change.DELETED:
                return "[-]";

            default:
                return ""; 
        }
    }
}