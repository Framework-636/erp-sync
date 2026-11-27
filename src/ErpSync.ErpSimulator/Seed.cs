using System.Data;
using Microsoft.Data.SqlClient;

namespace ErpSync.ErpSimulator;

/// <summary>
/// Fills the stand-in ERP with about 5,000 made-up items and their stock in each warehouse.
/// A fixed random seed means every run builds exactly the same data, so the seed is repeatable.
/// </summary>
internal static class Seed
{
    private const int ItemCount = 5000;
    private const int RandomSeed = 20261127;

    public static async Task RunAsync(string connectionString, bool reset)
    {
        await using var db = new SqlConnection(connectionString);
        await db.OpenAsync();

        var existing = await CountRowsAsync(db, "dbo.Item");
        if (existing > 0 && !reset)
        {
            Console.WriteLine($"dbo.Item already has {existing:N0} rows, so nothing was changed.");
            Console.WriteLine("Run 'seed --reset' to delete everything and seed again.");
            return;
        }

        var random = new Random(RandomSeed);
        var today = DateTime.Today;
        var items = BuildItems(random, today);
        var stock = BuildStock(random, items, today);

        // One transaction: either all rows land, or none do.
        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();

        if (reset)
        {
            await using var delete = db.CreateCommand();
            delete.Transaction = tx;
            // ON DELETE CASCADE removes the ItemWarehouse rows too.
            delete.CommandText = "DELETE FROM dbo.Item;";
            await delete.ExecuteNonQueryAsync();
        }

        await BulkInsertAsync(db, tx, "dbo.Item", items);
        await BulkInsertAsync(db, tx, "dbo.ItemWarehouse", stock);
        await tx.CommitAsync();

        Console.WriteLine($"Seeded {items.Rows.Count:N0} items and {stock.Rows.Count:N0} warehouse rows.");
    }

    // ---------- Made-up reference data. No real brands, vendors, or item codes. ----------

    private sealed record ProductLine(string Code, string[] Modifiers, (string Noun, string Category)[] Products);

    private static readonly ProductLine[] ProductLines =
    [
        new("HDW", ["Zinc", "Stainless", "Brass", "Black Oxide", "Galvanized"],
            [("Hex Bolt", "Fasteners"), ("Wood Screw", "Fasteners"), ("Lag Screw", "Fasteners"), ("Flat Washer", "Fasteners"),
             ("Lock Nut", "Fasteners"), ("Butt Hinge", "Hinges"), ("Strap Hinge", "Hinges"), ("Corner Brace", "Brackets"), ("Shelf Bracket", "Brackets")]),
        new("PLB", ["Brass", "PVC", "Copper", "CPVC", "Black Iron"],
            [("90 Elbow", "Fittings"), ("Tee", "Fittings"), ("Coupling", "Fittings"), ("Cap", "Fittings"), ("Male Adapter", "Fittings"),
             ("Ball Valve", "Valves"), ("Check Valve", "Valves"), ("Pipe Nipple", "Pipe")]),
        new("ELC", ["Gray", "White", "Ivory", "Weatherproof", "Steel"],
            [("Wire Nut", "Connectors"), ("Cable Clamp", "Connectors"), ("Junction Box", "Boxes"), ("Outlet Box", "Boxes"),
             ("Toggle Switch", "Devices"), ("Duplex Outlet", "Devices"), ("Wall Plate", "Devices"), ("Conduit Strap", "Conduit")]),
        new("JAN", ["Commercial", "Economy", "Heavy Duty", "Unscented", "Premium"],
            [("Trash Liner", "Liners"), ("Paper Towel Roll", "Paper"), ("Toilet Tissue", "Paper"), ("Mop Head", "Tools"),
             ("Scrub Pad", "Tools"), ("Floor Cleaner", "Chemicals"), ("Hand Soap", "Chemicals"), ("Spray Bottle", "Tools")]),
        new("PKG", ["Kraft", "Clear", "White", "Heavy Duty", "Recycled"],
            [("Shipping Box", "Boxes"), ("Packing Tape", "Tape"), ("Bubble Roll", "Fill"), ("Void Fill Paper", "Fill"),
             ("Poly Mailer", "Mailers"), ("Padded Mailer", "Mailers"), ("Stretch Wrap", "Wrap")]),
        new("SAF", ["Hi-Vis", "Disposable", "Reusable", "Anti-Fog", "Cut-Resistant"],
            [("Nitrile Glove", "Gloves"), ("Work Glove", "Gloves"), ("Safety Glasses", "Eyewear"), ("Goggles", "Eyewear"),
             ("Ear Plugs", "Hearing"), ("Safety Vest", "Apparel"), ("Floor Sign", "Signs")]),
    ];

    private static readonly string[] Sizes = ["1/4 in", "3/8 in", "1/2 in", "3/4 in", "1 in", "2 in", "Small", "Medium", "Large", "X-Large"];
    private static readonly string[] Brands = ["Ironleaf", "Bluestem", "Kestrel Pro", "Granite Peak", "Fernway", "Tallgrass", "Redwing Supply Co", "Lakeshore Basics"];
    private static readonly string[] Users = ["IMPORT", "ADMIN", "BUYER1", "BUYER2", "WH01"];
    private static readonly string[] NoteTexts = ["Check case pack on next PO", "Price change pending", "Customer favorite", "Do not reorder", "Count discrepancy last cycle"];
    private static readonly string[] Warehouses = ["MAIN", "EAST", "WEST"];

    // ---------- Item rows ----------

    private static DataTable BuildItems(Random random, DateTime today)
    {
        var table = new DataTable();
        table.Columns.Add("ItemCode", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("ExtendedDescription", typeof(string));
        table.Columns.Add("ProductLine", typeof(string));
        table.Columns.Add("Category", typeof(string));
        table.Columns.Add("Brand", typeof(string));
        table.Columns.Add("UnitOfMeasure", typeof(string));
        table.Columns.Add("CasePack", typeof(int));
        table.Columns.Add("UpcCode", typeof(string));
        table.Columns.Add("Weight", typeof(decimal));
        table.Columns.Add("Length", typeof(decimal));
        table.Columns.Add("Width", typeof(decimal));
        table.Columns.Add("Height", typeof(decimal));
        table.Columns.Add("StandardCost", typeof(decimal));
        table.Columns.Add("ListPrice", typeof(decimal));
        table.Columns.Add("VendorNumber", typeof(string));
        table.Columns.Add("VendorItemCode", typeof(string));
        table.Columns.Add("Status", typeof(string));
        table.Columns.Add("Taxable", typeof(bool));
        table.Columns.Add("Notes", typeof(string));
        table.Columns.Add("UserUpdated", typeof(string));
        table.Columns.Add("DateCreated", typeof(DateTime));
        table.Columns.Add("DateUpdated", typeof(DateTime));

        for (var i = 0; i < ItemCount; i++)
        {
            var line = Pick(random, ProductLines);
            var (noun, category) = Pick(random, line.Products);
            var modifier = Pick(random, line.Modifiers);
            var size = Pick(random, Sizes);
            var brand = Pick(random, Brands);
            var description = $"{modifier} {noun} {size}";

            var uom = Pick(random, new[] { "EA", "EA", "EA", "BX", "CS", "PK" });
            var casePack = uom == "EA" ? 1 : Pick(random, new[] { 6, 10, 12, 24, 50, 100 });

            // Costs spread from about $0.25 to $100, with most items cheap, like a real catalog.
            var cost = Math.Round((decimal)Math.Exp(random.NextDouble() * 6 - 1.4), 2);
            var price = Math.Round(cost * (decimal)(1.35 + random.NextDouble() * 0.85), 2);

            var created = today.AddDays(-random.Next(0, 3000));
            DateTime? updated = random.NextDouble() < 0.3 ? null : created.AddDays(random.Next(0, (today - created).Days + 1));

            var status = random.NextDouble() switch
            {
                < 0.92 => "A",
                < 0.97 => "I",
                _ => "D",
            };

            table.Rows.Add(
                $"{line.Code}-{10001 + i}",
                description,
                random.NextDouble() < 0.4 ? (object)$"{description}, {brand}. Sold by the {uom}." : DBNull.Value,
                line.Code,
                category,
                random.NextDouble() < 0.85 ? (object)brand : DBNull.Value,
                uom,
                casePack,
                random.NextDouble() < 0.8 ? (object)MakeUpc(random) : DBNull.Value,
                Math.Round((decimal)(0.05 + random.NextDouble() * 40), 3),
                Math.Round((decimal)(1 + random.NextDouble() * 35), 2),
                Math.Round((decimal)(1 + random.NextDouble() * 24), 2),
                Math.Round((decimal)(0.5 + random.NextDouble() * 18), 2),
                cost,
                price,
                $"V{random.Next(100, 120)}",
                $"{(char)('A' + random.Next(26))}{(char)('A' + random.Next(26))}{random.Next(1000, 99999)}",
                status,
                random.NextDouble() < 0.95,
                random.NextDouble() < 0.1 ? (object)Pick(random, NoteTexts) : DBNull.Value,
                Pick(random, Users),
                created,
                updated.HasValue ? (object)updated.Value : DBNull.Value);
        }

        return table;
    }

    // ---------- ItemWarehouse rows ----------

    private static DataTable BuildStock(Random random, DataTable items, DateTime today)
    {
        var table = new DataTable();
        table.Columns.Add("ItemCode", typeof(string));
        table.Columns.Add("WarehouseCode", typeof(string));
        table.Columns.Add("QuantityOnHand", typeof(decimal));
        table.Columns.Add("QuantityOnSalesOrder", typeof(decimal));
        table.Columns.Add("QuantityOnPurchaseOrder", typeof(decimal));
        table.Columns.Add("QuantityBackordered", typeof(decimal));
        table.Columns.Add("ReorderPoint", typeof(decimal));
        table.Columns.Add("BinLocation", typeof(string));
        table.Columns.Add("LastSoldDate", typeof(DateTime));
        table.Columns.Add("LastReceiptDate", typeof(DateTime));

        foreach (DataRow item in items.Rows)
        {
            var itemCode = (string)item["ItemCode"];

            foreach (var warehouse in Warehouses)
            {
                // Every item is stocked in MAIN; fewer are carried in the branch warehouses.
                var carried = warehouse switch
                {
                    "MAIN" => true,
                    "EAST" => random.NextDouble() < 0.7,
                    _ => random.NextDouble() < 0.45,
                };
                if (!carried)
                {
                    continue;
                }

                decimal onHand = random.NextDouble() < 0.15 ? 0 : random.Next(1, 601);
                decimal onSalesOrder = random.NextDouble() < 0.5 ? 0 : Math.Floor(onHand * (decimal)(random.NextDouble() * 0.3));
                decimal onPurchaseOrder = random.NextDouble() < 0.75 ? 0 : random.Next(10, 301);
                decimal backordered = onHand == 0 && random.NextDouble() < 0.3 ? random.Next(1, 51) : 0;

                table.Rows.Add(
                    itemCode,
                    warehouse,
                    onHand,
                    onSalesOrder,
                    onPurchaseOrder,
                    backordered,
                    (decimal)random.Next(0, 101),
                    $"{(char)('A' + random.Next(12))}{random.Next(1, 40):00}-{random.Next(1, 6)}",
                    random.NextDouble() < 0.8 ? (object)today.AddDays(-random.Next(0, 120)) : DBNull.Value,
                    random.NextDouble() < 0.7 ? (object)today.AddDays(-random.Next(0, 365)) : DBNull.Value);
            }
        }

        return table;
    }

    // ---------- Helpers ----------

    private static T Pick<T>(Random random, T[] options) => options[random.Next(options.Length)];

    /// <summary>A 12-digit UPC-A shaped number with a valid check digit. Made up, not registered to anyone.</summary>
    private static string MakeUpc(Random random)
    {
        var digits = new int[11];
        for (var i = 0; i < 11; i++)
        {
            digits[i] = random.Next(10);
        }

        // UPC check digit: 3 x (sum of odd positions) + (sum of even positions), then round up to a multiple of 10.
        var sum = 0;
        for (var i = 0; i < 11; i++)
        {
            sum += i % 2 == 0 ? digits[i] * 3 : digits[i];
        }
        var check = (10 - sum % 10) % 10;

        return string.Concat(digits) + check;
    }

    private static async Task<int> CountRowsAsync(SqlConnection db, string table)
    {
        await using var count = db.CreateCommand();
        count.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (int)(await count.ExecuteScalarAsync())!;
    }

    /// <summary>
    /// SqlBulkCopy streams all rows to the server in one go, which is far faster than 15,000 separate INSERTs.
    /// Columns are matched by name, so their order in the DataTable doesn't matter.
    /// </summary>
    private static async Task BulkInsertAsync(SqlConnection db, SqlTransaction tx, string tableName, DataTable rows)
    {
        using var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = tableName,
        };
        foreach (DataColumn column in rows.Columns)
        {
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }
        await bulk.WriteToServerAsync(rows);
    }
}
