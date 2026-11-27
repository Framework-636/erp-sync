using ErpSync.ErpSimulator;
using Microsoft.Extensions.Configuration;

// Settings come from .NET user secrets on this PC, never from a file in the repo.
var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .Build();

var connectionString = config.GetConnectionString("ErpStandIn");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Missing the ConnectionStrings:ErpStandIn secret. See 'Running it locally' in the README.");
    return 1;
}

// The first word after "--" picks what to do, e.g. dotnet run --project src/ErpSync.ErpSimulator -- init
var command = args.FirstOrDefault()?.ToLowerInvariant();

switch (command)
{
    case "init":
        await Init.RunAsync(connectionString);
        return 0;

    case "seed":
        // "seed --reset" deletes all items first; plain "seed" refuses to touch a table that already has rows.
        await Seed.RunAsync(connectionString, reset: args.Contains("--reset"));
        return 0;

    case "run":
    {
        // Optional number of seconds between rounds of changes: "run 5". Defaults to 3.
        var seconds = args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed > 0 ? parsed : 3;

        // Ctrl+C normally kills the program on the spot. Here it asks the loop to finish its round and exit cleanly.
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        await Simulate.RunAsync(connectionString, TimeSpan.FromSeconds(seconds), stop.Token);
        return 0;
    }

    default:
        Console.WriteLine("Usage: dotnet run --project src/ErpSync.ErpSimulator -- <command>");
        Console.WriteLine();
        Console.WriteLine("  init            Create the ErpStandIn database and its tables. Safe to run again.");
        Console.WriteLine("  seed            Fill an empty database with about 5,000 made-up items.");
        Console.WriteLine("  seed --reset    Delete all items, then seed again.");
        Console.WriteLine("  run [seconds]   Change quantities every few seconds (default 3) until Ctrl+C.");
        return 1;
}
