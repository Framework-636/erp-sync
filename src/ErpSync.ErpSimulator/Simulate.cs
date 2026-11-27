using Microsoft.Data.SqlClient;

namespace ErpSync.ErpSimulator;

/// <summary>
/// Pretends to be a busy warehouse: every few seconds it sells, receives, and counts stock,
/// and now and then changes a price, adds an item, or removes one. Runs until Ctrl+C.
/// </summary>
internal static class Simulate
{
    private static readonly string[] ProductLines = ["HDW", "PLB", "ELC", "JAN", "PKG", "SAF"];

    public static async Task RunAsync(string connectionString, TimeSpan interval, CancellationToken stop)
    {
        // Not seeded: unlike the seed step, every run should behave differently.
        var random = new Random();

        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync(stop);

        Console.WriteLine($"Simulating ERP activity every {interval.TotalSeconds:0.#} seconds. Press Ctrl+C to stop.");

        // PeriodicTimer ticks on a steady beat, and WaitForNextTickAsync throws once 'stop' is cancelled.
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                var changesThisTick = random.Next(1, 6);
                for (var i = 0; i < changesThisTick; i++)
                {
                    // Roll a number from 0 to 1 and pick an event. Sales are most common, adds and removes are rare.
                    var message = random.NextDouble() switch
                    {
                        < 0.55 => await SellAsync(db, random),
                        < 0.80 => await ReceiveAsync(db, random),
                        < 0.90 => await AdjustAsync(db, random),
                        < 0.96 => await ChangePriceAsync(db, random),
                        < 0.98 => await AddItemAsync(db, random),
                        _ => await RemoveItemAsync(db),
                    };

                    if (message is not null)
                    {
                        Console.WriteLine($"{DateTime.Now:HH:mm:ss}  {message}");
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C: leave the loop quietly.
        }

        Console.WriteLine("Stopped.");
    }

    // ---------- Events ----------

    /// <summary>A customer buys some. If there isn't enough on hand, the order goes on backorder instead.</summary>
    private static async Task<string?> SellAsync(SqlConnection db, Random random)
    {
        var row = await PickStockRowAsync(db);
        if (row is null)
        {
            return null;
        }
        var (item, warehouse) = row.Value;
        var qty = random.Next(1, 13);

        var sold = await ExecuteAsync(db, """
            UPDATE dbo.ItemWarehouse
            SET QuantityOnHand = QuantityOnHand - @qty,
                LastSoldDate   = CAST(GETDATE() AS DATE)
            WHERE ItemCode = @item AND WarehouseCode = @warehouse AND QuantityOnHand >= @qty;
            """, ("@qty", qty), ("@item", item), ("@warehouse", warehouse));

        if (sold > 0)
        {
            return $"SOLD       {item,-10} {warehouse,-4}  -{qty}";
        }

        await ExecuteAsync(db, """
            UPDATE dbo.ItemWarehouse
            SET QuantityBackordered = QuantityBackordered + @qty
            WHERE ItemCode = @item AND WarehouseCode = @warehouse;
            """, ("@qty", qty), ("@item", item), ("@warehouse", warehouse));
        return $"BACKORDER  {item,-10} {warehouse,-4}  +{qty} backordered";
    }

    /// <summary>A delivery arrives: stock goes up and the open purchase-order quantity goes down.</summary>
    private static async Task<string?> ReceiveAsync(SqlConnection db, Random random)
    {
        var row = await PickStockRowAsync(db);
        if (row is null)
        {
            return null;
        }
        var (item, warehouse) = row.Value;
        var qty = random.Next(12, 145);

        await ExecuteAsync(db, """
            UPDATE dbo.ItemWarehouse
            SET QuantityOnHand          = QuantityOnHand + @qty,
                QuantityOnPurchaseOrder = CASE WHEN QuantityOnPurchaseOrder > @qty
                                               THEN QuantityOnPurchaseOrder - @qty ELSE 0 END,
                LastReceiptDate         = CAST(GETDATE() AS DATE)
            WHERE ItemCode = @item AND WarehouseCode = @warehouse;
            """, ("@qty", qty), ("@item", item), ("@warehouse", warehouse));
        return $"RECEIVED   {item,-10} {warehouse,-4}  +{qty}";
    }

    /// <summary>A cycle count finds the shelf doesn't match the system, so on-hand is corrected a little.</summary>
    private static async Task<string?> AdjustAsync(SqlConnection db, Random random)
    {
        var row = await PickStockRowAsync(db);
        if (row is null)
        {
            return null;
        }
        var (item, warehouse) = row.Value;
        var delta = random.Next(1, 6) * (random.Next(2) == 0 ? -1 : 1);

        await ExecuteAsync(db, """
            UPDATE dbo.ItemWarehouse
            SET QuantityOnHand = CASE WHEN QuantityOnHand + @delta < 0 THEN 0 ELSE QuantityOnHand + @delta END
            WHERE ItemCode = @item AND WarehouseCode = @warehouse;
            """, ("@delta", delta), ("@item", item), ("@warehouse", warehouse));
        return $"ADJUSTED   {item,-10} {warehouse,-4}  {delta:+#;-#}";
    }

    /// <summary>Someone in purchasing edits a price. This changes the wide Item table, not stock.</summary>
    private static async Task<string?> ChangePriceAsync(SqlConnection db, Random random)
    {
        var item = await PickItemAsync(db);
        if (item is null)
        {
            return null;
        }
        // Between 10% down and 15% up.
        var factor = Math.Round(0.90m + (decimal)random.NextDouble() * 0.25m, 3);

        await ExecuteAsync(db, """
            UPDATE dbo.Item
            SET ListPrice   = ROUND(ListPrice * @factor, 2),
                DateUpdated = CAST(GETDATE() AS DATE),
                UserUpdated = N'SIM'
            WHERE ItemCode = @item;
            """, ("@factor", factor), ("@item", item));
        return $"PRICE      {item,-10}       x{factor}";
    }

    /// <summary>A new item is set up and stocked in MAIN.</summary>
    private static async Task<string?> AddItemAsync(SqlConnection db, Random random)
    {
        // Every code looks like ABC-12345, so the number starts at character 5.
        var highest = await ScalarAsync(db, "SELECT MAX(CAST(SUBSTRING(ItemCode, 5, 10) AS INT)) FROM dbo.Item;");
        var number = highest is int n ? n + 1 : 10001;
        var line = ProductLines[random.Next(ProductLines.Length)];
        var item = $"{line}-{number}";
        var cost = Math.Round(0.5m + (decimal)random.NextDouble() * 40m, 2);
        var qty = random.Next(24, 200);

        // XACT_ABORT plus a transaction: if the second INSERT fails, the first is rolled back too.
        await ExecuteAsync(db, """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            INSERT INTO dbo.Item (ItemCode, Description, ProductLine, Category, StandardCost, ListPrice, UserUpdated, DateUpdated)
            VALUES (@item, @description, @line, N'New', @cost, ROUND(@cost * 1.6, 2), N'SIM', CAST(GETDATE() AS DATE));

            INSERT INTO dbo.ItemWarehouse (ItemCode, WarehouseCode, QuantityOnHand, ReorderPoint, LastReceiptDate)
            VALUES (@item, N'MAIN', @qty, 10, CAST(GETDATE() AS DATE));

            COMMIT TRANSACTION;
            """,
            ("@item", item), ("@description", $"New Stock Item {number}"), ("@line", line), ("@cost", cost), ("@qty", qty));
        return $"ADDED      {item,-10} MAIN  +{qty}";
    }

    /// <summary>An item is deleted. ON DELETE CASCADE removes its warehouse rows.</summary>
    private static async Task<string?> RemoveItemAsync(SqlConnection db)
    {
        var item = await PickItemAsync(db);
        if (item is null)
        {
            return null;
        }

        await ExecuteAsync(db, "DELETE FROM dbo.Item WHERE ItemCode = @item;", ("@item", item));
        return $"REMOVED    {item,-10}";
    }

    // ---------- Helpers ----------

    /// <summary>One random item-and-warehouse row. ORDER BY NEWID() shuffles the table; fine at this size.</summary>
    private static async Task<(string Item, string Warehouse)?> PickStockRowAsync(SqlConnection db)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT TOP (1) ItemCode, WarehouseCode FROM dbo.ItemWarehouse ORDER BY NEWID();";
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }
        return (reader.GetString(0), reader.GetString(1));
    }

    private static async Task<string?> PickItemAsync(SqlConnection db) =>
        await ScalarAsync(db, "SELECT TOP (1) ItemCode FROM dbo.Item ORDER BY NEWID();") as string;

    /// <summary>Runs a query that returns one value (or DBNull/null when there's no row).</summary>
    private static async Task<object?> ScalarAsync(SqlConnection db, string sql)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Runs a statement with named parameters and returns how many rows it changed.
    /// Parameters keep values out of the SQL text, which prevents SQL injection and lets SQL Server reuse the query plan.
    /// </summary>
    private static async Task<int> ExecuteAsync(SqlConnection db, string sql, params (string Name, object Value)[] parameters)
    {
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }
        return await cmd.ExecuteNonQueryAsync();
    }
}
