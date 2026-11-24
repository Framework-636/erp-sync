# Creates the project plan on GitHub: one label, seven milestones, and all issues.
# Safe to run more than once: anything that already exists is skipped.
#
# Run from the repo folder:
#   powershell -ExecutionPolicy Bypass -File .\scripts\create-plan.ps1

# ---------- checks ----------

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Host "GitHub CLI (gh) is not installed. Install it, then run this again." -ForegroundColor Red
    exit 1
}

gh auth status | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "You are not signed in. Run: gh auth login" -ForegroundColor Red
    exit 1
}

$repo = gh repo view --json nameWithOwner --jq ".nameWithOwner"
if ($LASTEXITCODE -ne 0 -or -not $repo) {
    Write-Host "Run this from inside the erp-sync folder." -ForegroundColor Red
    exit 1
}
Write-Host "Creating plan in $repo" -ForegroundColor Cyan

# ---------- the plan ----------

$milestones = @(
    @{ Title = "M0 - Setup";                               Due = "2026-11-24T20:00:00Z"; Desc = "Empty solution builds and is pushed to the repo." },
    @{ Title = "M1 - Stand-in ERP and simulator";          Due = "2026-11-29T20:00:00Z"; Desc = "Database holds about 5,000 items and quantities change on their own." },
    @{ Title = "M2 - Change detection and bridge service"; Due = "2026-12-04T20:00:00Z"; Desc = "Service logs exactly which rows were added, changed, or deleted each cycle, with passing tests." },
    @{ Title = "M3 - API and push";                        Due = "2026-12-09T20:00:00Z"; Desc = "A change in the ERP appears in the cloud copy within 30 seconds, running locally." },
    @{ Title = "M4 - Dashboard";                           Due = "2026-12-14T20:00:00Z"; Desc = "One page shows per-table status, recent changes, and failures." },
    @{ Title = "M5 - Deploy and pipeline";                 Due = "2026-12-18T20:00:00Z"; Desc = "Live on Azure, and a push to GitHub deploys automatically. Ready to show employers." },
    @{ Title = "M6 - Hardening";                           Due = $null;                  Desc = "Items from employer preferred lists, added one at a time after the live demo." }
)

$M0 = "M0 - Setup"
$M1 = "M1 - Stand-in ERP and simulator"
$M2 = "M2 - Change detection and bridge service"
$M3 = "M3 - API and push"
$M4 = "M4 - Dashboard"
$M5 = "M5 - Deploy and pipeline"
$M6 = "M6 - Hardening"

$issues = @(
    # M0
    @{ M = $M0; T = "Install the .NET 10 SDK and the C# extension for VS Code"; D = "The command dotnet --version prints a 10.x version, and VS Code shows C# syntax help." },
    @{ M = $M0; T = "Create the solution and empty projects"; D = "A solution with Bridge, Api, ErpSimulator, and test projects exists, and dotnet build succeeds." },
    @{ M = $M0; T = "Replace the README with the project overview and plan"; D = "README explains what the project is, how it works, and the milestone plan." },
    @{ M = $M0; T = "Set up per-machine secrets and document the setup steps"; D = "No password or key is in the repo, and the README lists the steps to run the project on a second computer." },

    # M1
    @{ M = $M1; T = "Run SQL Server in Docker for local development"; D = "One command starts a local SQL Server, and the steps are in the README." },
    @{ M = $M1; T = "Design the stand-in ERP tables"; D = "Tables for items and inventory by warehouse exist, including a wide item table like a real ERP has." },
    @{ M = $M1; T = "Seed about 5,000 fake items"; D = "A repeatable seed step fills the stand-in ERP with made-up items and quantities." },
    @{ M = $M1; T = "Build the simulator that changes quantities on a timer"; D = "While the simulator runs, quantities change every few seconds, and items are occasionally added or removed." },
    @{ M = $M1; T = "Write milestone 1 notes"; D = "A short file in docs/notes covers what was built and what was learned." },

    # M2
    @{ M = $M2; T = "Define the swappable ERP reader interface"; D = "The bridge depends on an interface, and the simulated ERP reader is one implementation of it." },
    @{ M = $M2; T = "Read rows using a narrow column list"; D = "Each table has a configured list of columns, and only those are read." },
    @{ M = $M2; T = "Fingerprint each row"; D = "The same row contents always give the same hash, and any change gives a different one." },
    @{ M = $M2; T = "Store fingerprints in a local SQLite file"; D = "Fingerprints survive a restart of the bridge." },
    @{ M = $M2; T = "Detect inserts, updates, and deletes each cycle"; D = "Given the previous and current fingerprints, the bridge produces the exact list of changes." },
    @{ M = $M2; T = "Run the bridge as a background service on a 15-second timer"; D = "The service runs until stopped and logs the changes found in each cycle." },
    @{ M = $M2; T = "Unit tests for change detection"; D = "Tests cover insert, update, delete, no change, and an empty table, and all pass." },
    @{ M = $M2; T = "Switch to a branch and pull request per issue"; D = "From here on, each issue is done on its own branch and merged through a pull request." },
    @{ M = $M2; T = "Write milestone 2 notes"; D = "A short file in docs/notes covers what was built and what was learned." },

    # M3
    @{ M = $M3; T = "Create the API project with a health endpoint"; D = "The API starts locally and a health address returns OK." },
    @{ M = $M3; T = "Design the cloud tables with revision numbers and soft deletes"; D = "Each synced table has a revision column and a deleted-at column." },
    @{ M = $M3; T = "Add database migrations for the cloud tables"; D = "One command creates or updates the cloud database from the code." },
    @{ M = $M3; T = "Ingest endpoint that accepts a batch of changes"; D = "Posting a batch of inserts, updates, and deletes updates the cloud tables." },
    @{ M = $M3; T = "Sign requests from the bridge and verify them in the API"; D = "Unsigned or tampered requests are rejected." },
    @{ M = $M3; T = "Make ingest safe to retry"; D = "Sending the same batch twice changes nothing the second time, and stale revisions are ignored." },
    @{ M = $M3; T = "Bridge pushes changes and retries on failure"; D = "If the API is down, the bridge keeps the changes and delivers them once it is back." },
    @{ M = $M3; T = "Record every sync run"; D = "Each run stores rows changed, duration, and any error." },
    @{ M = $M3; T = "End-to-end check: change appears within 30 seconds"; D = "A quantity change in the stand-in ERP shows in the cloud table within 30 seconds, running locally." },
    @{ M = $M3; T = "Write milestone 3 notes"; D = "A short file in docs/notes covers what was built and what was learned." },

    # M4
    @{ M = $M4; T = "Create the React and TypeScript dashboard project"; D = "The dashboard starts locally and shows a placeholder page." },
    @{ M = $M4; T = "Add API endpoints for dashboard data"; D = "Endpoints return per-table status, recent changes, and recent failures." },
    @{ M = $M4; T = "Per-table status view"; D = "Each table shows its row count, last sync time, and seconds behind." },
    @{ M = $M4; T = "Recent changes list"; D = "The page lists the latest changes and refreshes on its own." },
    @{ M = $M4; T = "Failures view"; D = "Failed runs are listed with their error and time." },
    @{ M = $M4; T = "Write milestone 4 notes"; D = "A short file in docs/notes covers what was built and what was learned." },

    # M5
    @{ M = $M5; T = "Create the Azure account and set a budget alert"; D = "An alert emails me before spending passes a small monthly limit." },
    @{ M = $M5; T = "Create the Azure SQL database"; D = "The cloud database exists and migrations have run against it." },
    @{ M = $M5; T = "Deploy the API to Azure"; D = "The health address works at a public web address." },
    @{ M = $M5; T = "Deploy the dashboard"; D = "The dashboard loads at a public web address and reads from the live API." },
    @{ M = $M5; T = "GitHub Actions: build and test on every push"; D = "Every push runs the build and tests, and a failure is visible on GitHub." },
    @{ M = $M5; T = "GitHub Actions: deploy on merge to main"; D = "Merging a pull request updates the live site with no manual steps." },
    @{ M = $M5; T = "Point the local bridge at the live API"; D = "Changes from the stand-in ERP on my PC appear on the live dashboard." },
    @{ M = $M5; T = "Add the live demo link and screenshots to the README"; D = "A visitor can open the demo from the README in one click." },
    @{ M = $M5; T = "Write milestone 5 notes"; D = "A short file in docs/notes covers what was built and what was learned." },

    # M6
    @{ M = $M6; T = "Message queue between the API and the database"; D = "Changes pass through Azure Service Bus, and failed messages land in a holding queue that the dashboard shows." },
    @{ M = $M6; T = "Microsoft sign-in on the dashboard"; D = "The dashboard requires a Microsoft Entra ID login." },
    @{ M = $M6; T = "Run the bridge as an installed Windows service"; D = "The bridge installs as a service, starts at boot, and restarts after a crash." },
    @{ M = $M6; T = "Architecture diagram and write-up in the README"; D = "The README explains the design decisions and trade-offs." },
    @{ M = $M6; T = "Record a 60-second demo"; D = "A short recording shows a change moving from the ERP to the dashboard." }
)

# ---------- label ----------

Write-Host ""
Write-Host "Label" -ForegroundColor Cyan
gh label create later --description "Good idea, outside the current scope" --color BFD4F2 --force | Out-Null
Write-Host "  ok: later"

# ---------- milestones ----------

Write-Host ""
Write-Host "Milestones" -ForegroundColor Cyan

$existingMs = @()
$json = (gh api "repos/$repo/milestones?state=all&per_page=100") -join "`n"
if ($LASTEXITCODE -eq 0 -and $json) {
    $parsed = $json | ConvertFrom-Json
    if ($parsed) { $existingMs = @($parsed | ForEach-Object { $_.title }) }
}

foreach ($m in $milestones) {
    if ($existingMs -contains $m.Title) {
        Write-Host "  skip (exists): $($m.Title)"
        continue
    }
    if ($m.Due) {
        gh api "repos/$repo/milestones" -f "title=$($m.Title)" -f "description=$($m.Desc)" -f "due_on=$($m.Due)" | Out-Null
    } else {
        gh api "repos/$repo/milestones" -f "title=$($m.Title)" -f "description=$($m.Desc)" | Out-Null
    }
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED: $($m.Title)" -ForegroundColor Red
        exit 1
    }
    Write-Host "  created: $($m.Title)"
}

# ---------- issues ----------

Write-Host ""
Write-Host "Issues" -ForegroundColor Cyan

$existingIssues = @()
$json = (gh issue list --state all --limit 500 --json title) -join "`n"
if ($LASTEXITCODE -eq 0 -and $json) {
    $parsed = $json | ConvertFrom-Json
    if ($parsed) { $existingIssues = @($parsed | ForEach-Object { $_.title }) }
}

$bodyFile = Join-Path $env:TEMP "erp-sync-issue-body.txt"
$created = 0
$skipped = 0

foreach ($i in $issues) {
    if ($existingIssues -contains $i.T) {
        $skipped++
        continue
    }
    $body = "**Done when:** " + $i.D
    [System.IO.File]::WriteAllText($bodyFile, $body)

    gh issue create --title $i.T --body-file $bodyFile --milestone $i.M | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED: $($i.T)" -ForegroundColor Red
        Write-Host "  Fix the problem above and run the script again. Finished items will be skipped."
        exit 1
    }
    $created++
    Write-Host "  created: $($i.T)"
    Start-Sleep -Seconds 1
}

Remove-Item $bodyFile -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done. Issues created: $created. Already existed: $skipped." -ForegroundColor Green
Write-Host "See them at: https://github.com/$repo/milestones"
