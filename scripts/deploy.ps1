param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\Competition.csproj"),
    [string]$PublishDir = (Join-Path $PSScriptRoot "..\artifacts\publish"),
    [string]$FtpHost = "d113wh.forpsi.com",
    [string]$RemoteDir = "/www",
    [string]$LogDir = (Join-Path $PSScriptRoot "..\deployment\logs"),
    [string]$CredsPath = (Join-Path $PSScriptRoot "..\deployment\ftp-creds.json"),
    [string]$RollbackCacheDir = (Join-Path $PSScriptRoot "..\deployment\rollback-cache")
)

$ErrorActionPreference = "Stop"

$null = New-Item -ItemType Directory -Force -Path $LogDir
$script:LogFile = Join-Path $LogDir ("deploy-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
$script:HadError = $false
$script:Credential = $null
$script:RollbackMap = @()

function Write-Log {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message,

        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS")]
        [string]$Level = "INFO"
    )

    $line = "[{0}] {1}" -f $Level, $Message
    Write-Host $line
    Add-Content -Path $script:LogFile -Value $line
}

function Write-Banner {
    param([Parameter(Mandatory = $true)][string]$Text)

    $separator = ("=" * 72)
    Write-Log $separator
    Write-Log $Text
    Write-Log $separator
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
        Write-Host "Uploaded $LocalPath"
    }
    finally {
        $response.Dispose()
    }
}

function Publish-App {
    param([string]$ResolvedProjectPath, [string]$OutputPath)

    if (Test-Path $OutputPath) {
        Remove-Item -Recurse -Force $OutputPath
    }

    New-Item -ItemType Directory -Force -Path $OutputPath | Out-Null
    dotnet publish $ResolvedProjectPath -c Release -o $OutputPath
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
    $null = New-Item -ItemType Directory -Force -Path $resolvedRollbackCacheDir

    Write-Log "Publishing project: $resolvedProjectPath"
    Publish-App -ResolvedProjectPath $resolvedProjectPath -OutputPath $resolvedPublishDir

    $remoteBaseUri = Get-FtpBaseUri -FtpHost $FtpHost -Path $RemoteDir
    $ensuredRemoteDirectories = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase
    )
    Write-Log "Ensuring remote folder: $remoteBaseUri"
    Ensure-RemoteDirectory -RemoteUri $remoteBaseUri -Credential $script:Credential -KnownDirectories $ensuredRemoteDirectories

    $files = Get-ChildItem -Path $resolvedPublishDir -File -Recurse | Where-Object {
        $_.Name -ne 'appsettings.Development.json' -and $_.Name -ne 'ftp-creds.json'
    }
    Write-Log ("Uploading {0} file(s)." -f $files.Count)
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

        try {
            Download-RemoteFile -RemoteUri $remoteFileUri -LocalPath $localBackupPath -Credential $script:Credential
            New-LocalRollbackEntry -RemoteUri $remoteFileUri -BackupPath $localBackupPath
            Delete-RemoteFile -RemoteUri $remoteFileUri -Credential $script:Credential
        }
        catch {
            $ftpResponse = Get-FtpErrorResponse -Exception $_.Exception
            if ($null -ne $ftpResponse -and
                $ftpResponse.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
                Write-Log "No existing remote file to back up: $remoteFileUri" -Level WARN
            }
            else {
                throw
            }
        }

        Upload-File -LocalPath $file.FullName -RemoteUri $remoteFileUri -Credential $script:Credential
    }

    Write-Banner "DEPLOYMENT SUCCESS"
    Write-Log ("Deployment complete. Log saved to {0}" -f $script:LogFile) -Level SUCCESS
}
catch {
    $script:HadError = $true
    Write-Banner "DEPLOYMENT FAILED"
    Write-Log $_.Exception.Message -Level ERROR
    if ($script:RollbackMap.Count -gt 0 -and $null -ne $script:Credential) {
        Write-Log "Restoring backups from local cache..." -Level WARN
        Restore-RollbackCache -Credential $script:Credential
    }
    Write-Log ("See log file: {0}" -f $script:LogFile) -Level ERROR
    throw
}
finally {
    if ($script:HadError) {
        Write-Log "Result: FAILED" -Level ERROR
    }
    else {
        Write-Log "Result: SUCCESS" -Level SUCCESS
    }
}
