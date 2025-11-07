using System.Diagnostics;
using System.Text;
using LibGit2Sharp;

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
    private static string[] targetFilePaths = [];
    private static Dictionary<string, string> previousFileHashes = new();
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

        foreach (var s in targetFilePaths)
        {
            var localHead = repository.Head.Tip;
            var localFile = localHead.Tree.First(x => x.Path == s).Target;
            var remoteHead = repository.Branches[branchName].Tip;
            var remoteFile = remoteHead.Tree.First(x => x.Path == s).Target;

            var output = previousFileHashes[s] != remoteFile.Sha && localFile.Sha != remoteFile.Sha;

            if (output)
                return true;
        }

        return false;
    }

    public static void InitializeGit(string repo, string[] filePaths, string remoteString, string branch)
    {
        repository = new Repository(repo);
        targetFilePaths = filePaths;
        branchName = branch;

        remote = repository.Network.Remotes[remoteString];
        var remoteHead = repository.Branches[branchName].Tip;

        foreach(var path in targetFilePaths)
        {
            var remoteFile = remoteHead.Tree.First(x => x.Path == "fetch_test.txt").Target; 
            previousFileHashes[path] = remoteFile.Sha;
        }
    }

    public static void UpdateHashToCurrent()
    {
        foreach (var path in targetFilePaths)
        {
            var remoteHead = repository.Branches[branchName].Tip;
            var remoteFile = remoteHead.Tree.First(x => x.Path == path).Target;
            previousFileHashes[path] = remoteFile.Sha;
        }
    }

    public static string GetCurrentHash(string pathName)
    {
        var remoteHead = repository.Branches[branchName].Tip;
        var remoteFile = remoteHead.Tree.First(x => x.Path == pathName).Target;
        return remoteFile.Sha; 
    }

    public static void Pull()
    {
        ReportConflicts(); 
        
        var mergeResult = Commands.Pull(repository, new Signature(new Identity("a", "b"), DateTime.Now), new() { MergeOptions = new() { MergeFileFavor = MergeFileFavor.Union, FailOnConflict = true, CommitOnSuccess = true } });

        if (mergeResult.Status == MergeStatus.Conflicts)
            Console.WriteLine("Conflicts detected... aborting merge. Please pull, merge the contents manually and reimport the new files.");
        else
            Console.WriteLine("Merge successfull.");

    }
    
    public static void ReportConflicts()
    {
        var list = new Dictionary<string, List<LineChange[]>>();

        var totalConflictCount = 0;

        foreach (var path in targetFilePaths)
        {
            var conflicts = GetConflictingLines(path);
            totalConflictCount += conflicts.Count(); 

            list.Add(path, conflicts);
        }

        if (list.Count() > 0)
        {
            Console.WriteLine($"Found a total of {totalConflictCount} conflicting lines in {list.Keys.Count()} files. By default, the line changes from both the local tree (your current working tree) and the remote tree (on the github servers) will get merged into the file. This is just a heads up.");

            var builder = new StringBuilder("Conflicting Lines:\n\n");

            foreach (var conflictFile in list)
            {
                foreach (var lines in conflictFile.Value)
                {
                    var local = lines[0];
                    var remote = lines[1];

                    builder.Append("-----------------------------\n");
                    builder.Append($"File: {conflictFile.Key}\n");
                    builder.Append($"Index: {local.Line.LineNumber}\n");
                    builder.Append($"Local: {local.Change.GetIdentifer()} {local.Line.Content.ReplaceLineEndings("")}\n");
                    builder.Append($"Remote: {remote.Change.GetIdentifer()} {remote.Line.Content.ReplaceLineEndings("")}\n");
                    builder.Append("-----------------------------\n\n");
                }

                Console.WriteLine(builder.ToString());   
            }
        }
    }

    // [0] = local, [1] = remote
    public static List<LineChange[]> GetConflictingLines(string path)
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

        var localFileChanges = localChanges[path];
        var remoteFileChanges = remoteChanges[path];

        if (localFileChanges == null || remoteFileChanges == null)
            return []; 

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
                    Password = File.ReadLines("config.txt").First()
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
        foreach (var path in targetFilePaths)
        {
            repository.Index.Add(path);
        }
        
        repository.Index.Write();

        Signature author = new Signature("a", "b", DateTime.Now);
        Signature commiter = author;

        try
        {
            Commit commit = repository.Commit("Comitted local " + targetFilePaths + " file changes", author, commiter);
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
        GitHubHelper.InitializeGit("test123", ["fetch_test.txt"], "origin", "refs/remotes/origin/main");
        GitHubHelper.Lock();
    }
}