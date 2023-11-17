function Delete-CacheFolders {
    param (
        [string]$FolderName
    )

    # Collect directories to be deleted
    $dirsToDelete = Get-ChildItem -Recurse -Directory -Filter $FolderName | Where-Object { $_.FullName -notlike '*node_modules*' }

    # Check if there are directories to delete
    if ($dirsToDelete.Count -eq 0) {
        Write-Host "No '$FolderName' folders to delete."
        return
    }

    # List the directories
    Write-Host "Following '$FolderName' folders will be deleted:"
    foreach ($dir in $dirsToDelete) {
        Write-Host $dir.FullName
    }

    # Prompt for user confirmation
    $safetyCheck = Read-Host "Proceed with deletion of '$FolderName' folders? [y/n]?"

    # Exit if user does not confirm
    if ($safetyCheck -ne 'y') {
        Write-Host "Operation aborted for '$FolderName' folders."
        return
    }

    # Delete directories
    foreach ($dir in $dirsToDelete) {
        Remove-Item $dir.FullName -Recurse -Force
        Write-Host "$($dir.FullName) has been deleted."
    }

    Write-Host "Deletion of '$FolderName' folders completed."
}

# Delete 'bin' folders
Delete-CacheFolders -FolderName 'bin'

# Ask if the user wants to delete 'obj' folders
$objDeletionCheck = Read-Host "Do you want to proceed with deleting 'obj' folders as well? [y/n]?"
if ($objDeletionCheck -eq 'y') {
    Delete-CacheFolders -FolderName 'obj'
}
