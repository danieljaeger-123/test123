public class ProgramB
{
    private bool exit;
    public ProgramB()
    {
        GitHubHelper.InitializeGit("../litma_github_test", ["fetch_test.txt"], "origin", "refs/remotes/origin/main");

        Task t = new Task(async () =>
        {
            while (!exit)
            {
                var hasChanged = GitHubHelper.HasChanged();

                if (hasChanged)
                {
                    Console.WriteLine("Change detected, press 'a' to accept or press literally anything else to ignore.");
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.A)
                    {
                        GitHubHelper.Fetch(); // fetch again to be sure we have the latest references

                        // technically this code has the following issue: if there are new changes since the notification, they will be ignored (when displaying changes) - the newest changes are still considered when merging, the user just won't know about them
                        // the following code would remedy that but its ugly and i dont know if we need / want to even display the incomming changes to the user

                        // var oldHash = GitHubHelper.previousFileHash; 
                        // GitHubHelper.Fetch(); // fetch again to be sure we have the latest references
                        // var newHash = GitHubHelper.GetCurrentHash(); 

                        // if(oldHash != newHash)
                        // {
                        //     Console.WriteLine("New changes found since last notification, aborting... please try again.")
                        // }
                        GitHubHelper.Commit();  
                        GitHubHelper.Pull();
                        GitHubHelper.Push().GetAwaiter().GetResult(); 
                    }

                    GitHubHelper.UpdateHashToCurrent();
                }

                Console.WriteLine("Searching for update..."); 

                await Task.Delay(5000);
            }
        });

        t.Start();

        while(!exit)
        { 
            var key = Console.ReadKey(true);

            if(key.Key == ConsoleKey.Escape)
                exit = true;
        }
    }
}