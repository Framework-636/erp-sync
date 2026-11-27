# M1: Stand-in ERP and simulator

**Goal:** a SQL Server database that holds about 5,000 items, with quantities that change on their own.
**Finished:** November 27, 2026

## What I did

1. Ran SQL Server 2022 in Docker, with the password kept in a git-ignored `.env` file (#5).
2. Designed two tables shaped like a legacy ERP: `Item` and `ItemWarehouse` (#6).
3. Gave the simulator an `init` command that creates the database and tables, safe to run again (#6).
4. Added a `seed` command that fills in 5,000 made-up items and about 10,800 stock rows, the same every time (#7).
5. Added a `run` command that sells, receives, counts, re-prices, adds and removes items every few seconds until Ctrl+C (#8).

## The files, and what each one is for

### `docker-compose.yml`

Describes the SQL Server container so one command (`docker compose up -d`) starts it.

- `image: mcr.microsoft.com/mssql/server:2022-latest` is Microsoft's official SQL Server for Linux.
- `ACCEPT_EULA` and `MSSQL_PID: Developer` accept the license and pick the free, full-featured edition.
- `MSSQL_SA_PASSWORD: ${MSSQL_SA_PASSWORD}` is filled in from `.env`, so the password is never in git.
- `ports: "127.0.0.1:1433:1433"` publishes the port to this PC only, not to the rest of the network.
- `volumes: erp-sql-data` keeps the database files in a named Docker volume, so data survives the container stopping or being recreated.

### `.env` (not in git)

One line: `MSSQL_SA_PASSWORD=...`. The `*.env` rule in `.gitignore` keeps it out of the repo. SQL Server only reads it the first time it creates its data, so changing it later does nothing on its own (see Problems).

### `src/ErpSync.ErpSimulator/Sql/schema.sql`

The two tables, written as plain SQL so the schema is easy to read.

- `IF OBJECT_ID(...) IS NULL` creates each table only if it's missing, so running it twice is harmless. There is no `DROP` anywhere.
- **`Item`** is wide on purpose (about 25 columns), like a real ERP. It's keyed on `ItemCode`, a code a person typed, not an auto-number. Some columns (`Notes`, `UserUpdated`, `DateCreated`) are clutter the bridge will never read.
- `DateUpdated` is a **date with no time**. You can't ask it "what changed in the last 15 seconds?", which is the reason the bridge in M2 fingerprints rows instead.
- **`ItemWarehouse`** has one row per item per warehouse (`MAIN`, `EAST`, `WEST`), keyed on both columns. `ON DELETE CASCADE` removes an item's stock rows when the item is deleted.
- Money is `DECIMAL(19, 4)` and quantities are `DECIMAL(16, 3)`. Never `FLOAT`, which can't store 0.10 exactly.
- Every constraint is named (`PK_`, `FK_`, `DF_`, `CK_`), so errors are readable and later changes can refer to them. `CHECK` constraints reject bad values like a status of `X`.

### `src/ErpSync.ErpSimulator/ErpSync.ErpSimulator.csproj`

- `Microsoft.Data.SqlClient` is the driver that lets C# talk to SQL Server.
- `Microsoft.Extensions.Configuration.UserSecrets` lets a plain console app read user secrets. The Bridge and Api get this from their templates; a console app doesn't.
- `UserSecretsId` links the project to its secrets folder on my PC.
- `<None Update="Sql\**\*.sql" CopyToOutputDirectory="PreserveNewest" />` copies the SQL file next to the built program, because the program runs from `bin\Debug\net10.0\`, not the source folder.

### `src/ErpSync.ErpSimulator/Program.cs`

Reads the connection string from user secrets, then looks at the first word after `--` (`init`, `seed`, `run`) and calls the matching class. Anything else prints the usage text. For `run`, it sets up Ctrl+C so it cancels the loop cleanly instead of killing the program mid-query.

### `src/ErpSync.ErpSimulator/Init.cs`

1. Connects to `master` (the server's built-in database, since ours may not exist yet) and creates `ErpStandIn` if it's missing. A database name can't be a query parameter, so `QUOTENAME` brackets it safely and the `CREATE` runs as dynamic SQL.
2. Reconnects to `ErpStandIn` and runs `schema.sql`.
3. Lists the tables to show it worked.

### `src/ErpSync.ErpSimulator/Seed.cs`

- `new Random(20261127)`: a fixed seed gives the same "random" sequence every run, so `seed --reset` always rebuilds the same 5,000 items. That makes the seed repeatable and gives M2 tests known data.
- All names are invented: six product lines (HDW, PLB, ELC, JAN, PKG, SAF), brands like "Ironleaf", codes like `HDW-10042`. UPCs have a valid check digit but aren't registered to anyone.
- The data is shaped like a real catalog: mostly cheap items, every item in MAIN but fewer in EAST and WEST, about 15% out of stock, and some fields left empty.
- Rows are built in a `DataTable` and sent with `SqlBulkCopy`, which streams them in one go instead of about 16,000 separate `INSERT`s. `CheckConstraints` makes it obey the `CHECK` rules.
- The delete (for `--reset`) and both inserts share one transaction: all of it lands, or none of it.
- Plain `seed` refuses to run if items already exist, so I can't wipe data by accident.

### `src/ErpSync.ErpSimulator/Simulate.cs`

Every 3 seconds (or `run 5` for 5), it makes 1 to 5 changes. Out of 100 events, roughly:

- 55 sales (on-hand down; if there isn't enough stock, the quantity goes to backorder instead)
- 25 receipts (on-hand up, open purchase-order quantity down)
- 10 cycle-count adjustments (a small plus or minus)
- 6 price changes on the wide `Item` table
- 2 new items, and 2 deleted items. A deleted code can come back later as a new item, an edge case the bridge must handle.

Ideas in it:

- **Query parameters** (`@qty`, `@item`) keep values out of the SQL text. That prevents SQL injection and lets SQL Server reuse the query plan.
- **Check and update in one statement** (`WHERE ... AND QuantityOnHand >= @qty`), so stock can't go negative. The returned row count (0 = not enough stock) decides whether it becomes a backorder.
- **`SET XACT_ABORT ON` plus a transaction** when adding an item, so a failed second `INSERT` rolls back the first.
- **`PeriodicTimer`** ticks on a steady beat; cancelling its token ends the loop with `OperationCanceledException`, which is caught to exit cleanly.

## Secrets

The simulator now has its own user secrets:

| Setting                        | Example                                                                                         |
| ------------------------------ | ----------------------------------------------------------------------------------------------- |
| `ConnectionStrings:ErpStandIn` | `Server=127.0.0.1,1433;Database=ErpStandIn;User Id=sa;Password=...;TrustServerCertificate=True` |

`TrustServerCertificate=True` accepts the container's self-signed certificate. That's fine for local development only, never for Azure.

## Problems I hit

- **Docker engine not running.** `docker info` failed to connect to `dockerDesktopLinuxEngine`. Docker Desktop was installed but not started; opening it fixed it.
- **`no configuration file provided`.** I ran `docker compose up -d` before creating `docker-compose.yml`. Compose looks for the file in the current folder.
- **`Keyword not supported` in the connection string.** My password contained `;`, which separates connection-string settings, so the parser split the password in two. Lesson: avoid `;` (and `$` or quotes, which Compose and PowerShell treat specially) in passwords that live in connection strings.
- **Changing the password.** Editing `.env` doesn't change the password of an existing database, because SQL Server only reads it once, when the volume is first created. Before there was data, I used `docker compose down -v` (`-v` deletes the volume) and started over. Once there was data, I changed it in place with `ALTER LOGIN sa WITH PASSWORD = N'...'` in an interactive `sqlcmd` session, then updated `.env` and the user secret to match.
- **Timeout connecting to `localhost`.** The container was healthy (`Test-NetConnection 127.0.0.1 -Port 1433` succeeded), but the app timed out. The port is published on the IPv4 loopback only, and "localhost" can resolve to IPv6 first. Using `Server=127.0.0.1,1433` fixed it.
- **`Could not find a part of the path ...bin\Debug\net10.0\Sql\schema.sql`.** The `.csproj` copy rule was right, but `schema.sql` had never been saved into the `Sql` folder. `Get-ChildItem -Recurse ... -Filter *schema*` finding nothing gave it away.

## What I learned

- A container being "Up" doesn't mean the service inside is ready. Check the logs and run a real query.
- Narrow down connection problems in layers: inside the container (`docker exec ... sqlcmd`), then the port (`Test-NetConnection`), then the app's connection string.
