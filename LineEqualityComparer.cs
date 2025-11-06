using System.Diagnostics.CodeAnalysis;
using LibGit2Sharp;

public class LineEqualityComparer : IEqualityComparer<Line>
{
    public bool Equals(Line x, Line y)
    {
        return x.LineNumber == y.LineNumber; 
    }

    public int GetHashCode([DisallowNull] Line obj)
    {
        return (obj.LineNumber.GetHashCode() / Int32.MaxValue) * obj.Content.GetHashCode(); 
    }
}