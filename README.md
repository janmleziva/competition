# Competition

Small ASP.NET Core countdown app for the competition landing page.

## Azure publish

1. Open `Competition.sln` in Visual Studio.
2. Sign in to Azure.
3. Right-click the project and choose `Publish`.
4. Select `Azure` and then `Azure App Service`.
5. Create or select your App Service.
6. Publish.

## FTP deployment

Use `scripts/deploy.ps1` to publish and upload the app to the FTP host.

Required local file:

- `deployment/ftp-creds.json`

Defaults used by the script:

- `FTP_HOST = d113wh.forpsi.com`
- `REMOTE_DIR = /www`

### Run it

Create `deployment/ftp-creds.json` with this shape:

```json
{
  "username": "mlezikcz",
  "password": "<your-ftp-password>"
}
```

Then run from the repository root:

```powershell
.\deployment\deploy.cmd
```

If you prefer to call PowerShell directly:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy.ps1
```

You can also override the defaults if needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\deploy.ps1 `
  -FtpHost "d113wh.forpsi.com" `
  -RemoteDir "/www"
```

Each deployment writes a timestamped log file to `deployment/logs/`. The folder is intentionally ignored by Git so local run logs stay on your machine.

## Local run

Run the project from Visual Studio or with `dotnet run` from this folder.
