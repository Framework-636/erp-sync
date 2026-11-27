using Microsoft.Data.SqlClient;

namespace ErpSync.ErpSimulator;

/// <summary>
/// Creates the stand-in ERP database if it's missing, then runs Sql/schema.sql against it.
/// Every step checks before it creates, so running it twice changes nothing.
/// </summary>
internal static class Init
{
    public static async Task RunAsync(string connectionString)
    {
        // Pull the database name out of the connection string (Database=ErpStandIn).
        var builder = new SqlConnectionStringBuilder(connectionString);
        var databaseName = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("The connection string needs a Database=... part.");
        }

        // Step 1: our database may not exist yet, so we can't connect to it.
        // Connect to "master", the server's built-in database, and create ours from there.
        builder.InitialCatalog = "master";
        await using (var master = new SqlConnection(builder.ConnectionString))
        {
            await master.OpenAsync();

            await using var create = master.CreateCommand();
            // A database name can't be passed as a parameter, so QUOTENAME wraps it in [brackets]
            // safely and the CREATE runs as dynamic SQL.
            create.CommandText = """
                IF DB_ID(@name) IS NULL
                BEGIN
                    DECLARE @sql NVARCHAR(300) = N'CREATE DATABASE ' + QUOTENAME(@name);
                    EXEC (@sql);
                END;
                """;
            create.Parameters.AddWithValue("@name", databaseName);
            await create.ExecuteNonQueryAsync();
        }
        Console.WriteLine($"Database {databaseName} is ready.");

        // Step 2: connect to our database and run the schema file.
        // The build copies Sql/schema.sql next to the compiled program, which is where AppContext.BaseDirectory points.
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "Sql", "schema.sql");
        var schemaSql = await File.ReadAllTextAsync(schemaPath);

        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync();

        await using (var schema = db.CreateCommand())
        {
            schema.CommandText = schemaSql;
            await schema.ExecuteNonQueryAsync();
        }

        // Step 3: list the tables so you can see it worked.
        await using var list = db.CreateCommand();
        list.CommandText = "SELECT name FROM sys.tables ORDER BY name;";
        await using var reader = await list.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Console.WriteLine($"  table: {reader.GetString(0)}");
        }
    }
}
