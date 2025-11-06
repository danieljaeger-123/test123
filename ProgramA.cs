
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;

public class CommandHelper
{
    public static void ExecuteCommand(string[] command)
    {
        var psi = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var console = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start process");

        console.StandardInput.AutoFlush = true;

        foreach (string s in command)
        {
            console.StandardInput.WriteLine(s);
        }

        console.StandardInput.WriteLine("exit");

        string output = console.StandardOutput.ReadToEnd();
        Console.WriteLine("Output: " + output);
    }
}

public class GitHubHelper
{
    private static Repository repository = null!;
    public static string previousFileHash = "";
    private static string targetFilePath = "";
    private static string branchName = "";
    private static Remote remote = null!;

    public static void Fetch()
    {
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
        Commands.Fetch(repository, remote.Name, refSpecs, null, "");
    }
    public static bool HasChanged()
    {
        Fetch(); 

        var localHead = repository.Head.Tip;
        var localFile = localHead.Tree.First(x => x.Path == targetFilePath).Target;
        var remoteHead = repository.Branches[branchName].Tip;
        var remoteFile = remoteHead.Tree.First(x => x.Path == targetFilePath).Target;

        return previousFileHash != remoteFile.Sha && localFile.Sha != remoteFile.Sha;
    }

    public static void InitializeGit(string repo, string filePath, string remoteString, string branch)
    {
        repository = new Repository(repo);
        targetFilePath = filePath;
        branchName = branch;

        remote = repository.Network.Remotes[remoteString];
        var remoteHead = repository.Branches[branchName].Tip;
        var remoteFile = remoteHead.Tree.First(x => x.Path == "fetch_test.txt").Target;

        previousFileHash = remoteFile.Sha;
    }

    public static void UpdateHashToCurrent()
    {
        var remoteHead = repository.Branches[branchName].Tip;
        var remoteFile = remoteHead.Tree.First(x => x.Path == targetFilePath).Target;
        previousFileHash = remoteFile.Sha;
    }

    public static string GetCurrentHash()
    {
        var remoteHead = repository.Branches[branchName].Tip;
        var remoteFile = remoteHead.Tree.First(x => x.Path == targetFilePath).Target;
        return remoteFile.Sha; 
    }

    public static void Pull()
    {
        var conflictingLines = GetConflictingLines();

        if (conflictingLines.Count() > 0)
        {
            Console.WriteLine($"Found {conflictingLines.Count()} conflicting lines. By default, the line changes from both the local tree (your current working tree) and the remote tree (on the github servers) will get merged into the file. This is just a heads up.");

            var builder = new StringBuilder("Conflicting Lines:\n\n");

            foreach (var lines in conflictingLines)
            {
                var local = lines[0];
                var remote = lines[1];

                builder.Append("-----------------------------\n");
                builder.Append($"Index: {local.Line.LineNumber}\n");
                builder.Append($"Local: {local.Change.GetIdentifer()} {local.Line.Content}"); 
                builder.Append($"Remote: {remote.Change.GetIdentifer()} {remote.Line.Content}");
                builder.Append("-----------------------------\n");
                
            }

            Console.WriteLine(builder.ToString()); 
        }

        var mergeResult = Commands.Pull(repository, new Signature(new Identity("a", "b"), DateTime.Now), new() { MergeOptions = new() { MergeFileFavor = MergeFileFavor.Union, FailOnConflict = true, CommitOnSuccess = true } });

        if (mergeResult.Status == MergeStatus.Conflicts)
            Console.WriteLine("Conflicts detected... aborting merge. Please pull, merge the contents manually and reimport the new files.");
        else
            Console.WriteLine("Merge successfull.");

    }

    // string[0] = local, string[1] = remote
    public static List<LineChange[]> GetConflictingLines()
    {
        var localHead = repository.Head.Tip;
        var remoteHead = repository.Branches[branchName].Tip;
        var mergeBase = repository.ObjectDatabase.FindMergeBase(localHead, remoteHead);

        if (mergeBase == null)
        {
            Console.WriteLine("Couldn't find merge base... aborting searching for conflicts.");
            return [];
        }

        var localChanges = repository.Diff.Compare<Patch>(mergeBase.Tree, localHead.Tree);
        var remoteChanges = repository.Diff.Compare<Patch>(mergeBase.Tree, remoteHead.Tree);

        var localFileChanges = localChanges[targetFilePath];
        var remoteFileChanges = remoteChanges[targetFilePath];

        var localLines = localFileChanges.AddedLines.Concat(localFileChanges.DeletedLines);
        var remoteLines = remoteFileChanges.AddedLines.Concat(remoteFileChanges.DeletedLines);

        var conflictingLines = localLines.Intersect(remoteLines, new LineEqualityComparer());

        List<LineChange[]> output = conflictingLines.Select(x => new LineChange[]
        {
            new LineChange() { Line = localLines.First(y => y.LineNumber == x.LineNumber), Change = GetChangeType(localLines.First(y => y.LineNumber == x.LineNumber), localFileChanges.AddedLines, localFileChanges.DeletedLines) },
            new LineChange() { Line = remoteLines.First(y => y.LineNumber == x.LineNumber), Change = GetChangeType(remoteLines.First(y => y.LineNumber == x.LineNumber), remoteFileChanges.AddedLines, remoteFileChanges.DeletedLines) }
        }
        ).ToList();

        return output;
    }
    
    private static Change GetChangeType(Line line, List<Line> addedLines, List<Line> deletedLines)
    {
        bool added = addedLines.Contains(line);
        bool deleted = deletedLines.Contains(line);

        return added ? Change.ADDED : deleted ? Change.DELETED : Change.NONE; 
    }

    public static async Task Push()
    {
        var options = new PushOptions
        {
            CredentialsProvider = (url, user, types) =>
                new UsernamePasswordCredentials
                {
                    Username = "danieljaeger-123",
                    Password = "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"
                }
        };

        var local = repository.Head;
        if (local == null)
        {
            Console.WriteLine("No HEAD branch found.");
            return;
        }

        var localRef = local.CanonicalName;            // e.g. "refs/heads/main"
        var remoteRef = local.UpstreamBranchCanonicalName  // if it tracks an upstream, reuse that
                       ?? $"refs/heads/{local.FriendlyName}"; // fallback: push to same name on remote
        var refSpec = $"{localRef}:{remoteRef}";

        try
        {
            Console.WriteLine($"Pushing refspec '{refSpec}' to remote '{remote.Name}'...");
            repository.Network.Push(remote, refSpec, options);
            Console.WriteLine("Push completed.");
        }
        catch (Exception e)
        {
            Console.WriteLine("Exception during push: " + e.Message);
            Console.WriteLine(e.ToString());
        }

    }

    // TODO: make work for multiple files 
    public static void Commit()
    {
        repository.Index.Add(targetFilePath);
        repository.Index.Write();

        Signature author = new Signature("a", "b", DateTime.Now);
        Signature commiter = author;

        try
        {
            Commit commit = repository.Commit("Comitted local " + targetFilePath + " file changes", author, commiter);
        }
        catch (EmptyCommitException) { }
    }

    public static void Lock()
    {
        CommandHelper.ExecuteCommand(["cd test123", "git lfs lock fetch_test.txt"]);
    }

    public static void Unlock()
    {
        CommandHelper.ExecuteCommand(["cd test123", "git lfs unlock fetch_test.txt"]);
    }
}

public class ProgramA
{
    public ProgramA()
    {
        GitHubHelper.InitializeGit("test123", "fetch_test.txt", "origin", "refs/remotes/origin/main");
        GitHubHelper.Lock();
    }
}