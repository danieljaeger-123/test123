
using System.Diagnostics;
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
    private static string previousFileHash = "";
    private static string targetFilePath = "";
    private static string branchName = "";
    private static Remote remote = null!;
    public static bool HasChanged()
    {
        var refSpecs = remote.FetchRefSpecs.Select(x => x.Specification);
        Commands.Fetch(repository, remote.Name, refSpecs, null, "");

        // var localHead = repo.Head.Tip;
        // var localFile = localHead.Tree.First(x => x.Path == "fetch_test.txt").Target;
        // var remoteHead = repo.Branches[branchName].Tip; 
        // var remoteFile = remoteHead.Tree.First(x => x.Path == "fetch_test.txt").Target;
        // var differences = repo.Diff.Compare((Blob)localFile, (Blob)remoteFile);

        // return differences.LinesAdded != 0 || differences.LinesDeleted != 0;

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

    public static void Pull()
    {
        var mergeResult = Commands.Pull(repository, new Signature(new Identity("a", "b"), DateTime.Now), new() { MergeOptions = new() { FailOnConflict = true } });

        if (mergeResult.Status == MergeStatus.Conflicts)
            Console.WriteLine("Conflicts detected... aborting merge. Please pull, merge the contents manually and reimport the new files.");
        else
            Console.WriteLine("Merge successfull.");

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
    private bool exit;
    public ProgramA()
    {
        GitHubHelper.InitializeGit("test123", "fetch_test.txt", "origin", "refs/remotes/origin/main");
        GitHubHelper.Lock(); 
    }
}