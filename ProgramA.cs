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

    public static void InitializeGit(string repo, string[] filePaths, string remoteString, string branch)
    {
        repository = new Repository(repo);
        targetFilePaths = filePaths;
        branchName = branch;

        remote = repository.Network.Remotes[remoteString];
        var remoteHead = repository.Branches[branchName].Tip;

        foreach (var path in targetFilePaths)
        {
            var remoteFile = remoteHead.Tree.First(x => x.Path == path).Target;
            previousFileHashes[path] = remoteFile.Sha;
        }
    }

    public static void Fetch()
    {
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
        Commands.Fetch(repository, remote.Name, refSpecs, null, "");
    }

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


    public static bool ReportProblems(ProblemReport report)
    {
        var conflicts = report.Conflicts;
        var duplicateIDs = report.DuplicateIDs;

        if (duplicateIDs.Values.Count() >= 0)
        {
            Console.WriteLine("Detected multiple entries having identical id's when merging local and remote branches: ");

            foreach (var file in duplicateIDs.Keys)
            {
                var builder = new StringBuilder();

                foreach (var id in duplicateIDs[file])
                {
                    builder.Append("-----------------------------\n");
                    builder.Append($"File: {file}\n");
                    builder.Append($"ID: {id}\n");
                    builder.Append("-----------------------------\n\n");
                }

                Console.WriteLine(builder.ToString());
            }

            Console.WriteLine("Do you still want to pull, merge and resolve identical ids (press 'a'), or do you want to ignore it and manually pull and resolve duplicate ids later on (press literally anything else)?");

            var key = Console.ReadKey();

            if (key.Key != ConsoleKey.A)
                return true;
        }

        if (conflicts.Count() > 0)
        {
            Console.WriteLine($"Found a total of {conflicts.Values.Count()} conflicting lines in {conflicts.Keys.Count()} files. By default, the line changes from both the local tree (your current working tree) and the remote tree (on the github servers) will get merged into the file. This is just a heads up.");

            var builder = new StringBuilder("Conflicting Lines:\n\n");

            foreach (var file in conflicts)
            {
                foreach (var lines in file.Value)
                {
                    var local = lines[0];
                    var remote = lines[1];

                    builder.Append("-----------------------------\n");
                    builder.Append($"File: {file.Key}\n");
                    builder.Append($"Index: {local.Line.LineNumber}\n");
                    builder.Append($"Local: {local.Change.GetIdentifer()} {local.Line.Content.ReplaceLineEndings("")}\n");
                    builder.Append($"Remote: {remote.Change.GetIdentifer()} {remote.Line.Content.ReplaceLineEndings("")}\n");
                    builder.Append("-----------------------------\n\n");
                }

                Console.WriteLine(builder.ToString());
            }
        }

        return false; 
    }

    public static ProblemReport CheckForProblems()
    {
        var conflictDictionary = new Dictionary<string, IEnumerable<LineChange[]>>();
        var duplicateIDDictionary = new Dictionary<string, IEnumerable<string>>();

        // setup
        var localHead = repository.Head.Tip;
        var remoteHead = repository.Branches[branchName].Tip;
        var mergeBase = repository.ObjectDatabase.FindMergeBase(localHead, remoteHead);

        if (mergeBase == null)
        {
            Console.WriteLine("Couldn't find merge base... aborting searching for conflicts.");
            return new ProblemReport();
        }

        // get changes from local and remote
        var localChanges = repository.Diff.Compare<Patch>(mergeBase.Tree, localHead.Tree);
        var remoteChanges = repository.Diff.Compare<Patch>(mergeBase.Tree, remoteHead.Tree);

        foreach (var path in targetFilePaths)
        {
            var localFileChanges = localChanges[path];
            var remoteFileChanges = remoteChanges[path];

            if (localFileChanges == null || remoteFileChanges == null)
                continue; 
        
            var conflicts = CheckForConflictingLines(path, localFileChanges, remoteFileChanges);

            if (conflicts.Count() != 0)
                conflictDictionary.Add(path, conflicts);

            var duplicateIDs = CheckForDuplicateIDs(path, localFileChanges, remoteFileChanges);

            if (duplicateIDs.Count() != 0)
                duplicateIDDictionary.Add(path, duplicateIDs);
        }

        return new ProblemReport() { Conflicts = conflictDictionary, DuplicateIDs = duplicateIDDictionary };
    }

    // [0] = local, [1] = remote
    public static List<LineChange[]> CheckForConflictingLines(string path, PatchEntryChanges localFileChanges, PatchEntryChanges remoteFileChanges)
    {
        // combine changes in order to know what ultimatley changed 
        var localLines = localFileChanges.AddedLines.Concat(localFileChanges.DeletedLines);
        var remoteLines = remoteFileChanges.AddedLines.Concat(remoteFileChanges.DeletedLines);

        // checking conflicts
        var conflictingLines = localLines.Intersect(remoteLines, new LineEqualityComparer());

        List<LineChange[]> output = conflictingLines.Select(x => new LineChange[]
        {
            new LineChange() { Line = localLines.First(y => y.LineNumber == x.LineNumber), Change = GetChangeType(localLines.First(y => y.LineNumber == x.LineNumber), localFileChanges.AddedLines, localFileChanges.DeletedLines) },
            new LineChange() { Line = remoteLines.First(y => y.LineNumber == x.LineNumber), Change = GetChangeType(remoteLines.First(y => y.LineNumber == x.LineNumber), remoteFileChanges.AddedLines, remoteFileChanges.DeletedLines) }
        }
        ).ToList();

        return output;
    }


    public static List<string> CheckForDuplicateIDs(string path, PatchEntryChanges localFileChanges, PatchEntryChanges remoteFileChanges)
    {
        var localHead = repository.Head.Tip;
        var remoteHead = repository.Branches[branchName].Tip;

        var localText = localHead.Tree[path].Target.Peel<Blob>().GetContentText();
        var remoteText = remoteHead.Tree[path].Target.Peel<Blob>().GetContentText();

        List<string> localLines = localText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).ToList();
        List<string> remoteLines = remoteText.Split(["\r\n", "\r", "\n"], StringSplitOptions.None).ToList();

        var localAddedLines = localFileChanges.AddedLines.Select(x => x.Content);
        var remoteAddedLines = remoteFileChanges.AddedLines.Select(x => x.Content);

        return Validator.ValidateTextIDs(remoteLines, localAddedLines).Concat(Validator.ValidateTextIDs(localLines, remoteAddedLines)).Distinct().ToList();
    }

    private static Change GetChangeType(Line line, List<Line> addedLines, List<Line> deletedLines)
    {
        bool added = addedLines.Contains(line);
        bool deleted = deletedLines.Contains(line);

        return added ? Change.ADDED : deleted ? Change.DELETED : Change.NONE;
    }

    public static void Pull()
    {
        var mergeResult = Commands.Pull(repository, new Signature(new Identity("a", "b"), DateTime.Now), new() { MergeOptions = new() { MergeFileFavor = MergeFileFavor.Union, FailOnConflict = true, CommitOnSuccess = true } });

        if (mergeResult.Status == MergeStatus.Conflicts)
            Console.WriteLine("Conflicts detected... aborting merge. Please pull, merge the contents manually and reimport the new files.");
        else
            Console.WriteLine("Merge successfull.");
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

}

public class ProgramA
{
    public ProgramA()
    {
        GitHubHelper.InitializeGit("test123", ["fetch_test.txt"], "origin", "refs/remotes/origin/main");
    }
}

public class Validator
{
    public static List<string> ValidateTextIDs(IEnumerable<string> entries, IEnumerable<string> toCheck)
    {
        List<string> entryIDs = entries.Select(x => x.Split(";")[0]).ToList();
        List<string> IDsToCheck = toCheck.Select(x => x.Split(";")[0]).ToList();

        return entryIDs.Intersect(IDsToCheck).ToList();
    }
}

public class ProblemReport
{
    public Dictionary<string, IEnumerable<LineChange[]>> Conflicts = new();
    public Dictionary<string, IEnumerable<string>> DuplicateIDs = new();
}