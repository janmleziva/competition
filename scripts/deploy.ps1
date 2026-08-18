param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\Competition.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\artifacts\publish"),
    [string]$FtpHost = "d113wh.forpsi.com",
    [string]$RemoteDir = "/subdoms/pohoda-cup",
    [string]$LogDir = (Join-Path $PSScriptRoot "..\deployment\logs"),
    [string]$CredsPath = (Join-Path $PSScriptRoot "..\deployment\ftp-creds.json"),
    [string]$RollbackCacheDir = (Join-Path $PSScriptRoot "..\deployment\rollback-cache"),
    [string]$DatabaseBackupDir = (Join-Path $PSScriptRoot "..\deployment\database-backups"),
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$SelfContained,
    [switch]$Backup,
    [switch]$InspectRemoteDependencies,
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

function Test-IsRetryableFtpException {
    param([Parameter(Mandatory = $true)][System.Exception]$Exception)

    $ftpResponse = Get-FtpErrorResponse -Exception $Exception
    if ($null -ne $ftpResponse -and
        $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
        return $false
    }

    return $Exception.Message -match 'Unable to connect|timed out|connection|closed|forcibly'
}

function Invoke-FtpWithRetry {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Operation,
        [Parameter(Mandatory = $true)][string]$Description
    )

    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            return & $Operation
        }
        catch {
            if ($attempt -eq 4 -or -not (Test-IsRetryableFtpException -Exception $_.Exception)) {
                throw
            }

            Write-Log "$Description failed on attempt ${attempt}; retrying." -Level WARN
            Start-Sleep -Seconds (2 * $attempt)
        }
    }
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

    Invoke-FtpWithRetry -Description "Download $RemoteUri" -Operation {
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

            Write-Log "Downloaded remote file to $LocalPath"
        }
        finally {
            $response.Dispose()
        }
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

function Test-RemoteFileExists {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    try {
        Invoke-FtpWithRetry -Description "Check $RemoteUri" -Operation {
            $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
            $request.Method = [System.Net.WebRequestMethods+Ftp]::GetFileSize
            $request.Credentials = $Credential
            $request.UseBinary = $true
            $request.UsePassive = $true
            $request.KeepAlive = $false

            $response = $request.GetResponse()
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

function Get-RemoteDirectoryFileNames {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    return Invoke-FtpWithRetry -Description "List $RemoteUri" -Operation {
        $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
        $request.Method = [System.Net.WebRequestMethods+Ftp]::ListDirectory
        $request.Credentials = $Credential
        $request.UsePassive = $true
        $request.KeepAlive = $false

        $response = $request.GetResponse()
        try {
            $reader = [System.IO.StreamReader]::new($response.GetResponseStream())
            try {
                $names = @{}
                while (-not $reader.EndOfStream) {
                    $listedPath = $reader.ReadLine().Trim().Replace('\\', '/').TrimEnd('/')
                    if (-not [string]::IsNullOrWhiteSpace($listedPath)) {
                        $names[[System.IO.Path]::GetFileName($listedPath)] = $true
                    }
                }
                return $names
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $response.Dispose()
        }
    }
}

function Rename-RemoteFile {
    param(
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][string]$NewFileName,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    Invoke-FtpWithRetry -Description "Rename $RemoteUri" -Operation {
        $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
        $request.Method = [System.Net.WebRequestMethods+Ftp]::Rename
        $request.RenameTo = $NewFileName
        $request.Credentials = $Credential
        $request.UsePassive = $true
        $request.KeepAlive = $false

        $response = $request.GetResponse()
        try {
            Write-Log "Renamed remote file: $RemoteUri -> $NewFileName"
        }
        finally {
            $response.Dispose()
        }
    }
}

function Upload-File {
    param(
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    Write-Log "Uploading to: $RemoteUri"
    Invoke-FtpWithRetry -Description "Upload $RemoteUri" -Operation {
        $request = [System.Net.FtpWebRequest]::Create($RemoteUri)
        $request.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
        $request.Credentials = $Credential
        $request.UseBinary = $true
        $request.UsePassive = $true
        $request.KeepAlive = $false

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
}

function Replace-RemoteFile {
    param(
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [Parameter(Mandatory = $true)][string]$RemoteUri,
        [Parameter(Mandatory = $true)][System.Net.NetworkCredential]$Credential
    )

    $remoteFileName = [System.IO.Path]::GetFileName(([Uri]$RemoteUri).AbsolutePath)
    $temporaryFileName = "$remoteFileName.deploytmp-$([Guid]::NewGuid().ToString('N'))"
    $swapFileName = "$remoteFileName.deploybak-$([Guid]::NewGuid().ToString('N'))"
    $remoteDirectoryUri = $RemoteUri.Substring(0, $RemoteUri.Length - $remoteFileName.Length)
    $temporaryUri = $remoteDirectoryUri + $temporaryFileName
    $swapUri = $remoteDirectoryUri + $swapFileName
    $swapCreated = $false
    $replacementCompleted = $false

    try {
        Upload-File -LocalPath $LocalPath -RemoteUri $temporaryUri -Credential $Credential
        Rename-RemoteFile -RemoteUri $RemoteUri -NewFileName $swapFileName -Credential $Credential
        $swapCreated = $true
        for ($attempt = 1; $attempt -le 3 -and -not $replacementCompleted; $attempt++) {
            try {
                Rename-RemoteFile -RemoteUri $temporaryUri -NewFileName $remoteFileName -Credential $Credential
                $replacementCompleted = $true
            }
            catch {
                $verifyPath = Join-Path ([System.IO.Path]::GetTempPath()) ("competition-verify-{0}.tmp" -f [Guid]::NewGuid().ToString('N'))
                try {
                    Download-RemoteFile -RemoteUri $RemoteUri -LocalPath $verifyPath -Credential $Credential
                    if ((Get-FileSha256 -Path $LocalPath) -eq (Get-FileSha256 -Path $verifyPath)) {
                        Write-Log "Remote file verified after interrupted rename: $RemoteUri"
                        $replacementCompleted = $true
                    }
                }
                catch {
                    Write-Log "Remote replacement verification failed for $RemoteUri on attempt ${attempt}."
                }
                finally {
                    Remove-Item -LiteralPath $verifyPath -Force -ErrorAction SilentlyContinue
                }

                if (-not $replacementCompleted -and $attempt -eq 3) {
                    throw
                }

                if (-not $replacementCompleted) {
                    Start-Sleep -Seconds (2 * $attempt)
                }
            }
        }
        $replacementCompleted = $true
    }
    catch {
        if ($swapCreated -and -not $replacementCompleted) {
            try {
                Rename-RemoteFile -RemoteUri $swapUri -NewFileName $remoteFileName -Credential $Credential
            }
            catch {
                Write-Log "Failed to restore swapped remote file: $RemoteUri" -Level ERROR
            }
        }

        try {
            Delete-RemoteFile -RemoteUri $temporaryUri -Credential $Credential
        }
        catch {
            Write-Log "Failed to delete temporary remote file: $temporaryUri" -Level WARN
        }

        throw
    }

    try {
        Delete-RemoteFile -RemoteUri $swapUri -Credential $Credential
    }
    catch {
        Write-Log "Could not delete remote swap backup: $swapUri" -Level WARN
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

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([System.BitConverter]::ToString($sha256.ComputeHash($stream))).Replace('-', '')
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
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

function Get-DeploymentFileMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $version = Get-ComparableFileVersion -Path $Path
    return [pscustomobject]@{
        Path    = ($RelativePath -replace '\\', '/')
        Sha256  = Get-FileSha256 -Path $Path
        Version = if ($null -eq $version) { $null } else { $version.ToString() }
        Length  = (Get-Item -LiteralPath $Path).Length
        Unknown = $false
    }
}

function Test-IsThirdPartyOrRuntimeFile {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $normalizedPath = $RelativePath -replace '\\', '/'
    if ($normalizedPath.StartsWith('runtimes/', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $fileName = [System.IO.Path]::GetFileName($normalizedPath)
    $extension = [System.IO.Path]::GetExtension($normalizedPath)
    return $extension -in @('.dll', '.exe') -and
        $fileName -notin @('Competition.dll', 'Competition.exe')
}

function Get-RemoteReplacementDecision {
    param(
        [Parameter(Mandatory = $true)]$LocalMetadata,
        [Parameter(Mandatory = $true)]$RemoteMetadata,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    if ($LocalMetadata.Sha256 -eq $RemoteMetadata.Sha256) {
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

    $localVersion = if ([string]::IsNullOrWhiteSpace($LocalMetadata.Version)) { $null } else { [version]$LocalMetadata.Version }
    $remoteVersion = if ([string]::IsNullOrWhiteSpace($RemoteMetadata.Version)) { $null } else { [version]$RemoteMetadata.Version }
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
    param(
        [string]$ResolvedProjectPath,
        [string]$OutputPath,
        [string]$RuntimeIdentifier,
        [bool]$SelfContained
    )

    if (Test-Path $OutputPath) {
        Remove-Item -Recurse -Force $OutputPath
    }

    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
    $publishArguments = @(
        'publish',
        $ResolvedProjectPath,
        '-c',
        'Release',
        '-o',
        $OutputPath
    )

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $publishArguments += @('-r', $RuntimeIdentifier)
    }

    $publishArguments += @('--self-contained', $SelfContained.ToString().ToLowerInvariant())
    if (-not $SelfContained) {
        $publishArguments += '-p:UseAppHost=false'
    }

    Write-Log ("dotnet {0}" -f ($publishArguments -join ' '))
    $publishOutput = & dotnet @publishArguments 2>&1
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
    if ($Backup) {
        $null = New-Item -ItemType Directory -Force -Path $resolvedRollbackCacheDir
        $null = New-Item -ItemType Directory -Force -Path $resolvedDatabaseBackupDir
        Write-Log "Backups and rollback are enabled." -Console
    }
    else {
        Write-Log "Backups are disabled. Pass -Backup to enable them." -Console
    }

    Write-Log "Publishing application..." -Console
    Write-Log "Publishing project: $resolvedProjectPath"
    $selfContainedBuild = [bool]$SelfContained
    $publishRuntimeIdentifier = $RuntimeIdentifier
    if ($selfContainedBuild) {
        Write-Log "Publishing self-contained app for $RuntimeIdentifier." -Console
    }
    else {
        Write-Log "Publishing framework-dependent app for the FORPSI .NET runtime." -Console
    }

    Publish-App `
        -ResolvedProjectPath $resolvedProjectPath `
        -OutputPath $resolvedPublishDir `
        -RuntimeIdentifier $publishRuntimeIdentifier `
        -SelfContained $selfContainedBuild

    $remoteBaseUri = Get-FtpBaseUri -FtpHost $FtpHost -Path $RemoteDir
    $ensuredRemoteDirectories = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    Write-Log "Ensuring remote folder: $remoteBaseUri"
    Ensure-RemoteDirectory -RemoteUri $remoteBaseUri -Credential $script:Credential -KnownDirectories $ensuredRemoteDirectories

    $manifestFileName = 'competition-deploy-manifest.json'
    $remoteManifestUri = $remoteBaseUri + $manifestFileName
    $remoteManifestByPath = @{}
    $manifestDownloadPath = Join-Path ([System.IO.Path]::GetTempPath()) ("competition-manifest-{0}.json" -f [Guid]::NewGuid().ToString('N'))
    try {
        Download-RemoteFile -RemoteUri $remoteManifestUri -LocalPath $manifestDownloadPath -Credential $script:Credential
        $remoteManifest = Get-Content -LiteralPath $manifestDownloadPath -Raw | ConvertFrom-Json
        foreach ($entry in @($remoteManifest.Files)) {
            if (-not [string]::IsNullOrWhiteSpace($entry.Path)) {
                $remoteManifestByPath[$entry.Path] = $entry
            }
        }
        Write-Log ("Loaded deployment manifest with {0} file(s)." -f $remoteManifestByPath.Count)
    }
    catch {
        $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
        if ($null -ne $ftpResponse -and
            $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
            Write-Log "No remote deployment manifest exists; protected dependencies will be inventoried once." -Level WARN
        }
        elseif ($_.Exception -is [System.Management.Automation.RuntimeException]) {
            Write-Log "The remote deployment manifest is invalid; protected dependencies will be inventoried once." -Level WARN
        }
        else {
            throw
        }
    }
    finally {
        Remove-Item -LiteralPath $manifestDownloadPath -Force -ErrorAction SilentlyContinue
    }

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
        $_.Name -ne 'app_offline.htm' -and
        $_.Name -ne $manifestFileName
    } | Sort-Object `
        @{ Expression = { if ($_.FullName.EndsWith('App_Data\competition.db', [StringComparison]::OrdinalIgnoreCase)) { 0 } else { 1 } } }, `
        FullName)
    $uploadedFileCount = 0
    $skippedFileCount = 0
    $finalManifestEntries = [System.Collections.Generic.List[object]]::new()
    $remoteDirectoryListings = @{}
    Write-Log ("Evaluating {0} published file(s)." -f $files.Count)
    foreach ($file in $files) {
        $relativePath = $file.FullName.Substring($resolvedPublishDir.Length).TrimStart('\')
        $remoteFileUri = ($remoteBaseUri + ($relativePath -replace '\\', '/'))
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
            if (Test-RemoteFileExists -RemoteUri $remoteFileUri -Credential $script:Credential) {
                if ($Backup) {
                    $databaseBackupPath = Join-Path $resolvedDatabaseBackupDir (
                        "competition-{0}.db" -f (Get-Date -Format "yyyyMMdd-HHmmss")
                    )
                    Download-RemoteFile `
                        -RemoteUri $remoteFileUri `
                        -LocalPath $databaseBackupPath `
                        -Credential $script:Credential
                    Write-Log "Production database backup saved to $databaseBackupPath" -Console
                }
                Write-Log "Preserving the existing production database; local seed was not uploaded." -Console
            }
            else {
                Write-Log "No production database exists; uploading the converted local database as the initial seed." -Level WARN
                Upload-File -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
                $uploadedFileCount++
                Write-Log "Uploaded App_Data/competition.db (initial database seed)." -Console
            }

            continue
        }

        $localMetadata = Get-DeploymentFileMetadata -Path $file.FullName -RelativePath $normalizedRelativePath
        $remoteContainingDirectoryUri = $remoteBaseUri
        if (-not [string]::IsNullOrWhiteSpace($relativeDirectory)) {
            $remoteContainingDirectoryUri += (($relativeDirectory -replace '\\', '/').Trim('/')) + '/'
        }
        if (-not $remoteDirectoryListings.ContainsKey($remoteContainingDirectoryUri)) {
            $remoteDirectoryListings[$remoteContainingDirectoryUri] = Get-RemoteDirectoryFileNames `
                -RemoteUri $remoteContainingDirectoryUri `
                -Credential $script:Credential
        }
        $remoteFileListed = $remoteDirectoryListings[$remoteContainingDirectoryUri].ContainsKey($file.Name)
        $remoteMetadata = $remoteManifestByPath[$normalizedRelativePath]
        if ($null -ne $remoteMetadata -and -not $remoteFileListed) {
            Write-Log "Manifest entry is stale because the remote file is missing: $normalizedRelativePath" -Level WARN
            $remoteMetadata = $null
        }
        $comparisonPath = $null
        $remoteFileExists = $remoteFileListed

        $isProtectedDependency = Test-IsThirdPartyOrRuntimeFile -RelativePath $normalizedRelativePath
        $requiresDependencyInspection = $isProtectedDependency -and (
            $null -eq $remoteMetadata -or
            ($InspectRemoteDependencies -and $remoteMetadata.Unknown -eq $true)
        )
        if ($requiresDependencyInspection -and $InspectRemoteDependencies) {
            $comparisonExtension = [System.IO.Path]::GetExtension($file.Name)
            $comparisonPath = Join-Path ([System.IO.Path]::GetTempPath()) ("competition-compare-{0}{1}" -f [Guid]::NewGuid().ToString('N'), $comparisonExtension)
            try {
                Download-RemoteFile -RemoteUri $remoteFileUri -LocalPath $comparisonPath -Credential $script:Credential
                $remoteFileExists = $true
                $remoteMetadata = Get-DeploymentFileMetadata -Path $comparisonPath -RelativePath $normalizedRelativePath
            }
            catch {
                $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
                if ($null -eq $ftpResponse -or
                    $ftpResponse.StatusCode -ne [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
                    throw
                }
            }
        }
        elseif ($requiresDependencyInspection) {
            if ($remoteFileListed) {
                $remoteFileExists = $true
                $remoteMetadata = [pscustomobject]@{
                    Path = $normalizedRelativePath
                    Sha256 = $null
                    Version = $null
                    Length = $null
                    Unknown = $true
                }
                Write-Log "Preserving untracked protected dependency ${relativePath}; pass -InspectRemoteDependencies for a one-time version comparison."
            }
        }

        if ($null -eq $remoteMetadata) {
            $replacementDecision = [pscustomobject]@{
                ShouldUpload = $true
                Reason = 'file is new or has no manifest entry'
            }
        }
        else {
            $replacementDecision = Get-RemoteReplacementDecision `
                -LocalMetadata $localMetadata `
                -RemoteMetadata $remoteMetadata `
                -RelativePath $normalizedRelativePath
        }

        if (-not $replacementDecision.ShouldUpload) {
            Write-Log "Skipping ${relativePath}: $($replacementDecision.Reason)."
            $skippedFileCount++
            $finalManifestEntries.Add($remoteMetadata)
            if ($null -ne $comparisonPath) {
                Remove-Item -LiteralPath $comparisonPath -Force -ErrorAction SilentlyContinue
            }
            continue
        }

        Write-Log "Replacing ${relativePath}: $($replacementDecision.Reason)."
        $backedUpExistingFile = $false
        if ($Backup) {
            $localBackupPath = Join-Path $resolvedRollbackCacheDir ($relativePath -replace '[\\/:*?"<>|]', '_')
            if ($null -ne $comparisonPath -and $remoteFileExists) {
                Copy-Item -LiteralPath $comparisonPath -Destination $localBackupPath -Force
                $backedUpExistingFile = $true
            }
            elseif ($remoteFileExists -or (Test-RemoteFileExists -RemoteUri $remoteFileUri -Credential $script:Credential)) {
                Download-RemoteFile -RemoteUri $remoteFileUri -LocalPath $localBackupPath -Credential $script:Credential
                $backedUpExistingFile = $true
            }

            if ($backedUpExistingFile) {
                New-LocalRollbackEntry -RemoteUri $remoteFileUri -BackupPath $localBackupPath
                Replace-RemoteFile -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
            }
            else {
                Upload-File -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
            }
        }
        else {
            Upload-File -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
        }

        if ($null -ne $comparisonPath) {
            Remove-Item -LiteralPath $comparisonPath -Force -ErrorAction SilentlyContinue
        }
        $uploadedFileCount++
        $finalManifestEntries.Add($localMetadata)
        Write-Log "Uploaded $normalizedRelativePath" -Console
    }

    $localManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) ("competition-manifest-{0}.json" -f [Guid]::NewGuid().ToString('N'))
    try {
        $deploymentManifest = [ordered]@{
            SchemaVersion = 1
            GeneratedUtc = [DateTime]::UtcNow.ToString('o')
            Files = $finalManifestEntries
        }
        $deploymentManifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $localManifestPath -Encoding UTF8
        Upload-File -LocalPath $localManifestPath -RemoteUri $remoteManifestUri -Credential $script:Credential
        Write-Log "Updated remote deployment manifest."
    }
    finally {
        Remove-Item -LiteralPath $localManifestPath -Force -ErrorAction SilentlyContinue
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
