#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [string]$ProjectPath = "src/CompetitionTracker/CompetitionTracker.csproj",
    [string]$PublishPath = "artifacts/publish"
)

$ErrorActionPreference = "Stop"

foreach ($name in @("FTP_HOST", "FTP_USER", "FTP_PASSWORD", "FTP_REMOTE_PATH")) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) {
        throw "Missing required environment variable: $name"
    }
}

$ftpHost = $env:FTP_HOST.TrimEnd("/")
$remoteRoot = $env:FTP_REMOTE_PATH.Trim("/").Replace("\\", "/")
$baseUri = "ftp://$ftpHost/$remoteRoot"

if (Test-Path $PublishPath) {
    Remove-Item $PublishPath -Recurse -Force
}

dotnet restore $ProjectPath
dotnet publish $ProjectPath -c Release -o $PublishPath --no-restore

$credentials = [System.Net.NetworkCredential]::new($env:FTP_USER, $env:FTP_PASSWORD)

function New-FtpDirectory {
    param([string]$Uri)

    try {
        $request = [System.Net.FtpWebRequest]::Create($Uri)
        $request.Credentials = $credentials
        $request.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
        $request.UseBinary = $true
        $response = $request.GetResponse()
        $response.Close()
    }
    catch [System.Net.WebException] {
        # FTP servers usually return an error if the directory already exists.
        if ($_.Exception.Response -ne $null) {
            $_.Exception.Response.Close()
        }
    }
}

function Send-FtpFile {
    param(
        [string]$LocalPath,
        [string]$Uri
    )

    $request = [System.Net.FtpWebRequest]::Create($Uri)
    $request.Credentials = $credentials
    $request.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $request.UseBinary = $true
    $request.KeepAlive = $false

    $bytes = [System.IO.File]::ReadAllBytes($LocalPath)
    $request.ContentLength = $bytes.Length
    $stream = $request.GetRequestStream()
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Close()

    $response = $request.GetResponse()
    $response.Close()
}

Get-ChildItem $PublishPath -Directory -Recurse | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($PublishPath, $_.FullName).Replace("\\", "/")
    New-FtpDirectory "$baseUri/$relative"
}

Get-ChildItem $PublishPath -File -Recurse | ForEach-Object {
    $relative = [System.IO.Path]::GetRelativePath($PublishPath, $_.FullName).Replace("\\", "/")
    Write-Host "Uploading $relative"
    Send-FtpFile $_.FullName "$baseUri/$relative"
}

Write-Host "Deployment upload completed. Verify the site at /health."
