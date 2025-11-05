public class ProgramB
{
    private bool exit;
    public ProgramB()
    {
        GitHubHelper.InitializeGit("../litma_github_test", "fetch_test.txt", "origin", "refs/remotes/origin/main");

        Task t = new Task(async () =>
        {
            while (!exit)
            {
                var hasChanged = GitHubHelper.HasChanged();

                if (hasChanged)
                {
                    Console.WriteLine("Change detected, press 'a' to accept or anything else to ignore.");
                    var key = Console.ReadKey(true);

                    if (key.Key == ConsoleKey.A)
                    {
                        GitHubHelper.Pull();
                        GitHubHelper.Push(); 
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