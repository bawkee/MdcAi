param(
    [Parameter(Mandatory=$true)]
    [string]$MigrationName
)

# Check if the dotnet-ef tool is installed
$efToolResult = dotnet ef --version

if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet-ef tool not found. Install it with 'dotnet tool install --global dotnet-ef'" -ForegroundColor Red
    exit
}

# Add the migration
Write-Host "Adding migration '$MigrationName'..."
dotnet ef migrations add $MigrationName

if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to add migration '$MigrationName'" -ForegroundColor Red
    exit
}

# Update the database
Write-Host "Updating database..."
dotnet ef database update --connection "Data Source=Chats.db"

if ($LASTEXITCODE -eq 0) {
    Write-Host "Database updated successfully!" -ForegroundColor Green
} else {
    Write-Host "Failed to update the database" -ForegroundColor Red
}
