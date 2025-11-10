using System.Diagnostics.CodeAnalysis;
using LibGit2Sharp;

public class TextIDEqualityComparer : IEqualityComparer<Line>
{
    public bool Equals(Line x, Line y)
    {
        return x.Content.Split(";")[0] == y.Content.Split(";")[0]; 
    }

    public int GetHashCode([DisallowNull] Line obj)
    {
        return obj.Content.Split(";")[0].GetHashCode(); 
    }
}