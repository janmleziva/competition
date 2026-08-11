param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\Competition.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\artifacts\publish"),
    [string]$FtpHost = "d113wh.forpsi.com",
    [string]$RemoteDir = "/www",
    [string]$LogDir = (Join-Path $PSScriptRoot "..\deployment\logs"),
    [string]$CredsPath = (Join-Path $PSScriptRoot "..\deployment\ftp-creds.json"),
    [string]$RollbackCacheDir = (Join-Path $PSScriptRoot "..\deployment\rollback-cache"),
    [string]$DatabaseBackupDir = (Join-Path $PSScriptRoot "..\deployment\database-backups"),
    [switch]$NoPause
)

$ErrorActionPreference = "Stop"

$null = New-Item -ItemType Directory -Force -Path $LogDir
$script:LogFile = Join-Path $LogDir ("deploy-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$script:HadError = $false
$script:Credential = $null
$script:RollbackMap = @()
$script:OfflineMarkerUploaded = $false
$script:OfflineMarkerUri = $null
$script:DeploymentStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS")]
        [string]$Level = "INFO",

        [switch]$Console
    )

    $line = "[{0}] {1}" -f $Level, $Message
    if ($Console -or $Level -in @("WARN", "ERROR")) {
        Write-Host $line
    }
    Add-Content -Path $script:LogFile -Value $line
}

function Write-Banner {
    param([Parameter(Mandatory = $true)][string]$Text)

    Write-Log $Text -Console
}

function Wait-ForExitAcknowledgement {
    if ($NoPause -or [Console]::IsInputRedirected) {
        return
    }

    Write-Host ""
    Write-Host "Press any key to close this window..."
    try {
        $null = [Console]::ReadKey($true)
    }
    catch {
        $null = Read-Host "Press Enter to close this window"
    }
}

function Get-FtpCredentials {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Missing FTP credentials file: $Path"
    }

    $creds = Get-Content -Path $Path -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($creds.username) -or [string]::IsNullOrWhiteSpace($creds.password)) {
        throw "FTP credentials file must contain 'username' and 'password' values."
    }

    return [System.Net.NetworkCredential]::new($creds.username, $creds.password)
}

function Get-FtpBaseUri {
    param(
        [string]$FtpHost,
        [string]$Path
    )

    $trimmedPath = $Path.Trim("/")
    if ([string]::IsNullOrWhiteSpace($trimmedPath)) {
        return "ftp://$FtpHost/"
    }

    return "ftp://$FtpHost/$trimmedPath/"
}

function Get-FtpErrorResponse {
    param([Parameter(Mandatory = $true)][System.Exception]$Exception)

    $currentException = $Exception
    while ($null -ne $currentException) {
        if ($currentException.Response -is [System.Net.FtpWebResponse]) {
            return $currentException.Response
        }

        $currentException = $currentException.InnerException
    }

    return $null
}

function Test-RemoteDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::ListDirectory
    $request.Credentials = $Credential
    $request.UsePassive = $true
    $request.KeepAlive = $false

    try {
        $response = $request.GetResponse()
        try {
            $responseStream = $response.GetResponseStream()
            if ($null -ne $responseStream) {
                try {
                    $responseStream.CopyTo([System.IO.Stream]::Null)
                }
                finally {
                    $responseStream.Dispose()
                }
            }
        }
        finally {
            $response.Dispose()
        }

        return $true
    }
    catch {
        $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
        if ($null -ne $ftpResponse -and
            $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
            return $false
        }

        throw
    }
}

function Ensure-RemoteDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential,
        [System.Collections.Generic.HashSet[string]]$KnownDirectories
    )

    $base = [Uri]$RemoteUri
    $segments = $base.AbsolutePath.Trim("/").Split("/", [System.StringSplitOptions]::RemoveEmptyEntries)
    $currentPath = ""

    foreach ($segment in $segments) {
        $currentPath = if ([string]::IsNullOrWhiteSpace($currentPath)) { $segment } else { "$currentPath/$segment" }
        $currentUri = "$($base.Scheme)://$($base.Authority)/$currentPath/"

        if ($null -ne $KnownDirectories -and $KnownDirectories.Contains($currentUri)) {
            continue
        }

        if (Test-RemoteDirectory -RemoteUri $currentUri -Credential $Credential) {
            if ($null -ne $KnownDirectories) {
                $null = $KnownDirectories.Add($currentUri)
            }
            continue
        }

        $request = [System.Net.FtpWebRequest]::Create($currentUri.TrimEnd('/'))
        $request.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
        $request.Credentials = $Credential
        $request.UseBinary = $true
        $request.UsePassive = $true
        $request.KeepAlive = $false

        try {
            $response = $request.GetResponse()
            $response.Dispose()
            Write-Log "Created remote directory: $currentUri"
            if ($null -ne $KnownDirectories) {
                $null = $KnownDirectories.Add($currentUri)
            }
        }
        catch {
            $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
            if ($null -ne $ftpResponse -and
                $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable -and
                (Test-RemoteDirectory -RemoteUri $currentUri -Credential $Credential)) {
                continue
            }

            throw
        }
    }
}

function Download-RemoteFile {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::DownloadFile
    $request.Credentials = $Credential
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.KeepAlive = $false

    $response = $request.GetResponse()
    try {
        $responseStream = $response.GetResponseStream()
        try {
            $fileStream = [System.IO.File]::Create($LocalPath)
            try {
                $responseStream.CopyTo($fileStream)
            }
            finally {
                $fileStream.Dispose()
            }
        }
        finally {
            $responseStream.Dispose()
        }

        Write-Log "Backed up remote file to $LocalPath"
    }
    finally {
        $response.Dispose()
    }
}

function Delete-RemoteFile {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::DeleteFile
    $request.Credentials = $Credential
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.KeepAlive = $false

    try {
        $response = $request.GetResponse()
        $response.Dispose()
        Write-Log "Deleted remote file: $RemoteUri"
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response -and $_.Exception.Response.StatusDescription -match '550') {
            Write-Log "No existing remote file to delete: $RemoteUri" -Level WARN
            return
        }

        throw
    }
}

function Upload-File {
    param(
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $request.Credentials = $Credential
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.KeepAlive = $false

    Write-Log "Uploading to: $RemoteUri"
    $bytes = [System.IO.File]::ReadAllBytes($LocalPath)
    $request.ContentLength = $bytes.Length

    $stream = $request.GetRequestStream()
    try {
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }

        $response = $request.GetResponse()
        try {
            Write-Log "Uploaded $LocalPath"
        }
    finally {
        $response.Dispose()
    }
}

function Format-ElapsedTime {
    param([Parameter(Mandatory = $true)][TimeSpan]$Elapsed)

    if ($Elapsed.TotalMinutes -ge 1) {
        return ("{0}m {1}s" -f [math]::Floor($Elapsed.TotalMinutes), $Elapsed.Seconds)
    }

    return ("{0:N1}s" -f $Elapsed.TotalSeconds)
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Get-ComparableFileVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
    if ([string]::IsNullOrWhiteSpace($versionInfo.FileVersion)) {
        return $null
    }

    $match = [regex]::Match($versionInfo.FileVersion, '^\s*(\d+(?:\.\d+){1,3})')
    if (-not $match.Success) {
        return $null
    }

    try {
        return [version]::Parse($match.Groups[1].Value)
    }
    catch {
        return $null
    }
}

function Test-IsThirdPartyOrRuntimeFile {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $normalizedPath = $RelativePath -replace '\\', '/'
    if ($normalizedPath.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    return [System.IO.Path]::GetExtension($normalizedPath).Equals('.dll', [StringComparison]::OrdinalIgnoreCase) -and
        -not [System.IO.Path]::GetFileName($normalizedPath).Equals('Competition.dll', [StringComparison]::OrdinalIgnoreCase)
}

function Get-RemoteReplacementDecision {
    param(
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [Parameter(Mandatory = $true)][string]$RemoteBackupPath,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $localHash = Get-FileSha256 -Path $LocalPath
    $remoteHash = Get-FileSha256 -Path $RemoteBackupPath
    if ($localHash -eq $remoteHash) {
        return [pscustomobject]@{
            ShouldUpload = $false
            Reason = 'content is unchanged'
        }
    }

    if (-not (Test-IsThirdPartyOrRuntimeFile -RelativePath $RelativePath)) {
        return [pscustomobject]@{
            ShouldUpload = $true
            Reason = 'application file content changed'
        }
    }

    $localVersion = Get-ComparableFileVersion -Path $LocalPath
    $remoteVersion = Get-ComparableFileVersion -Path $RemoteBackupPath
    if ($null -eq $localVersion -or $null -eq $remoteVersion) {
        return [pscustomobject]@{
            ShouldUpload = $false
            Reason = 'third-party/runtime version could not be compared'
        }
    }

    if ($localVersion -le $remoteVersion) {
        return [pscustomobject]@{
            ShouldUpload = $false
            Reason = "local version $localVersion is not newer than remote version $remoteVersion"
        }
    }

    return [pscustomobject]@{
        ShouldUpload = $true
        Reason = "local version $localVersion is newer than remote version $remoteVersion"
    }
}

function Publish-App {
    param([string]$ResolvedProjectPath, [string]$OutputPath)

    if (Test-Path $OutputPath) {
        Remove-Item -Recurse -Force $OutputPath
    }

    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
    $publishOutput = & dotnet publish $ResolvedProjectPath -c Release -o $OutputPath 2>&1
    foreach ($outputLine in $publishOutput) {
        Write-Log "dotnet publish: $outputLine"
    }
    if ($LASTEXITCODE -ne 0) {
        foreach ($outputLine in $publishOutput) {
            Write-Host $outputLine
        }
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

function New-LocalRollbackEntry {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][string]$BackupPath
    )

    $script:RollbackMap += [pscustomobject]@{
        RemoteUri  = $RemoteUri
        BackupPath = $BackupPath
    }
}

function Restore-RollbackCache {
    param([Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential)

    for ($i = $script:RollbackMap.Count - 1; $i -ge 0; $i--) {
        $entry = $script:RollbackMap[$i]
        if (Test-Path $entry.BackupPath) {
            try {
                Delete-RemoteFile -RemoteUri $entry.RemoteUri -Credential $Credential
                Upload-File -LocalPath $entry.BackupPath -RemoteUri $entry.RemoteUri -Credential $Credential
            }
            catch {
                Write-Log "Failed to restore $($entry.RemoteUri) from $($entry.BackupPath)" -Level ERROR
            }
        }
    }
}

try {
    Write-Banner "DEPLOYMENT STARTED"
    Write-Log "Log file: $script:LogFile"

    $resolvedCredsPath = (Resolve-Path $CredsPath).Path
    Write-Log "Loading FTP credentials from: $resolvedCredsPath"
    $script:Credential = Get-FtpCredentials -Path $resolvedCredsPath

    $resolvedProjectPath = (Resolve-Path $ProjectPath).Path
    $resolvedPublishDir = [System.IO.Path]::GetFullPath($PublishDir)
    $resolvedRollbackCacheDir = [System.IO.Path]::GetFullPath($RollbackCacheDir)
    $resolvedDatabaseBackupDir = [System.IO.Path]::GetFullPath($DatabaseBackupDir)
    $null = New-Item -ItemType Directory -Force -Path $resolvedRollbackCacheDir
    $null = New-Item -ItemType Directory -Force -Path $resolvedDatabaseBackupDir

    Write-Log "Publishing application..." -Console
    Write-Log "Publishing project: $resolvedProjectPath"
    Publish-App -ResolvedProjectPath $resolvedProjectPath -OutputPath $resolvedPublishDir

    $remoteBaseUri = Get-FtpBaseUri -FtpHost $FtpHost -Path $RemoteDir
    $ensuredRemoteDirectories = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    Write-Log "Ensuring remote folder: $remoteBaseUri"
    Ensure-RemoteDirectory -RemoteUri $remoteBaseUri -Credential $script:Credential -KnownDirectories $ensuredRemoteDirectories

    $offlineMarkerPath = Join-Path $resolvedPublishDir 'app_offline.htm'
    $script:OfflineMarkerUri = $remoteBaseUri + 'app_offline.htm'
    [System.IO.File]::WriteAllText(
        $offlineMarkerPath,
        '<!doctype html><title>Deployment in progress</title><p>The application will be back shortly.</p>'
    )
    Write-Log "Taking the application offline for deployment..." -Console
    Upload-File -LocalPath $offlineMarkerPath -RemoteUri $script:OfflineMarkerUri -Credential $script:Credential
    $script:OfflineMarkerUploaded = $true
    Start-Sleep -Seconds 2
    Write-Log "Site is offline." -Console

    $files = @(Get-ChildItem -Path $resolvedPublishDir -File -Recurse | Where-Object {
        $_.Name -ne 'appsettings.Development.json' -and
        $_.Name -ne 'ftp-creds.json' -and
        $_.Name -ne 'app_offline.htm'
    } | Sort-Object `
        @{ Expression = { if ($_.FullName.EndsWith('App_Data\competition.db', [StringComparison]::OrdinalIgnoreCase)) { 0 } else { 1 } } }, `
        FullName)
    $uploadedFileCount = 0
    $skippedFileCount = 0
    Write-Log ("Evaluating {0} published file(s)." -f $files.Count)
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($resolvedPublishDir.Length).TrimStart('\')
        $remoteFileUri = ($remoteBaseUri + ($relativePath -replace '\\', '/'))
        $localBackupPath = Join-Path $resolvedRollbackCacheDir ($relativePath -replace '[\\/:*?"<>|]', '_')
        $relativeDirectory = [System.IO.Path]::GetDirectoryName($relativePath)

        if (-not [string]::IsNullOrWhiteSpace($relativeDirectory)) {
            $remoteDirectoryUri = $remoteBaseUri + (($relativeDirectory -replace '\\', '/').Trim('/')) + '/'

            if (-not $ensuredRemoteDirectories.Contains($remoteDirectoryUri)) {
                Write-Log "Ensuring remote folder: $remoteDirectoryUri"
                Ensure-RemoteDirectory -RemoteUri $remoteDirectoryUri -Credential $script:Credential -KnownDirectories $ensuredRemoteDirectories
            }
        }

        $normalizedRelativePath = $relativePath -replace '\\', '/'
        if ($normalizedRelativePath -ieq 'App_Data/competition.db') {
            $databaseBackupPath = Join-Path $resolvedDatabaseBackupDir (
                "competition-{0}.db" -f (Get-Date -Format "yyyyMMdd-HHmmss")
            )

            try {
                Download-RemoteFile `
                    -RemoteUri $remoteFileUri `
                    -LocalPath $databaseBackupPath `
                    -Credential $script:Credential
                Write-Log "Preserving the existing production database; local seed was not uploaded." -Console
            }
            catch {
                $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
                if ($null -ne $ftpResponse -and
                    $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
                    Write-Log "No production database exists; uploading the converted local database as the initial seed." -Level WARN
                    Upload-File -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
                    $uploadedFileCount++
                    Write-Log "Uploaded App_Data/competition.db (initial database seed)." -Console
                }
                else {
                    throw
                }
            }

            continue
        }

        try {
            Download-RemoteFile -RemoteUri $remoteFileUri -LocalPath $localBackupPath -Credential $script:Credential
            $replacementDecision = Get-RemoteReplacementDecision `
                -LocalPath $file.FullName `
                -RemoteBackupPath $localBackupPath `
                -RelativePath $relativePath

            if (-not $replacementDecision.ShouldUpload) {
                Write-Log "Skipping ${relativePath}: $($replacementDecision.Reason)."
                $skippedFileCount++
                continue
            }

            Write-Log "Replacing ${relativePath}: $($replacementDecision.Reason)."
            New-LocalRollbackEntry -RemoteUri $remoteFileUri -BackupPath $localBackupPath
            Delete-RemoteFile -RemoteUri $remoteFileUri -Credential $script:Credential
        }
        catch {
            $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
            if ($null -ne $ftpResponse -and
                $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
                Write-Log "No existing remote file to back up: $remoteFileUri"
            }
            else {
                throw
            }
        }

        Upload-File -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
        $uploadedFileCount++
        Write-Log "Uploaded $normalizedRelativePath" -Console
    }

    Delete-RemoteFile -RemoteUri $script:OfflineMarkerUri -Credential $script:Credential
    $script:OfflineMarkerUploaded = $false
    Write-Log "Site is back online." -Console
    $script:DeploymentStopwatch.Stop()
    $elapsedText = Format-ElapsedTime -Elapsed $script:DeploymentStopwatch.Elapsed

    Write-Banner "DEPLOYMENT SUCCESS"
    Write-Log ("Uploaded {0} new or newer file(s); skipped {1} unchanged or non-newer file(s)." -f $uploadedFileCount, $skippedFileCount) -Level SUCCESS -Console
    Write-Log "Deployment completed in $elapsedText." -Level SUCCESS -Console
    Write-Log ("Deployment complete. Log saved to {0}" -f $script:LogFile)
}
catch {
    $script:HadError = $true
    Write-Banner "DEPLOYMENT FAILED"
    Write-Log $_.Exception.Message -Level ERROR
    if ($script:RollbackMap.Count -gt 0 -and $null -ne $script:Credential) {
        Write-Log "Restoring backups from local cache..." -Level WARN
        Restore-RollbackCache -Credential $script:Credential
    }
    if ($script:OfflineMarkerUploaded -and $null -ne $script:Credential) {
        Write-Log "Bringing the application back online after deployment failure." -Level WARN
        Delete-RemoteFile -RemoteUri $script:OfflineMarkerUri -Credential $script:Credential
        $script:OfflineMarkerUploaded = $false
        Write-Log "Site is back online after rollback." -Console
    }
    $script:DeploymentStopwatch.Stop()
    $elapsedText = Format-ElapsedTime -Elapsed $script:DeploymentStopwatch.Elapsed
    Write-Log "Deployment and rollback ended after $elapsedText." -Level ERROR
    Write-Log ("See log file: {0}" -f $script:LogFile) -Level ERROR
    throw
}
finally {
    Wait-ForExitAcknowledgement
}
