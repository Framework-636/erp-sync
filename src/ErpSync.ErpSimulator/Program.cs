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

    default:
        Console.WriteLine("Usage: dotnet run --project src/ErpSync.ErpSimulator -- <command>");
        Console.WriteLine();
        Console.WriteLine("  init   Create the ErpStandIn database and its tables. Safe to run again.");
        return 1;
}

