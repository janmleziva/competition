var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

app.Logger.LogInformation("Starting Competition app in {Environment}", app.Environment.EnvironmentName);

var competitionSettings = builder.Configuration.GetSection("Competition").Get<CompetitionSettings>() ?? new CompetitionSettings();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("The application encountered an error.");
        });
    });
    app.UseHsts();
}

app.UseHttpsRedirection();

app.MapGet("/", () =>
{
    var target = competitionSettings.GetTargetDate();
    var title = competitionSettings.Title;
    var targetDisplay = target.ToString("d MMMM yyyy, h:mm tt", System.Globalization.CultureInfo.InvariantCulture);

    return Results.Content($$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>{{title}}</title>
  <style>
    :root {
      color-scheme: light;
      --bg1: #0f172a;
      --bg2: #1e293b;
      --panel: rgba(255,255,255,0.92);
      --text: #0f172a;
      --muted: #475569;
      --accent: #2563eb;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      min-height: 100vh;
      display: grid;
      place-items: center;
      font-family: system-ui, -apple-system, Segoe UI, Roboto, sans-serif;
      background: radial-gradient(circle at top, #334155, var(--bg1) 50%, #020617);
      color: var(--text);
      padding: 24px;
    }
    .card {
      width: min(720px, 100%);
      background: var(--panel);
      border-radius: 24px;
      padding: 32px 24px;
      box-shadow: 0 24px 80px rgba(0,0,0,0.35);
      text-align: center;
    }
    h1 {
      margin: 0 0 12px;
      font-size: clamp(2rem, 6vw, 4rem);
      line-height: 1.05;
    }
    p {
      margin: 0 0 24px;
      color: var(--muted);
      font-size: 1.1rem;
    }
    .countdown {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 12px;
    }
    .unit {
      background: white;
      border-radius: 18px;
      padding: 18px 12px;
      border: 1px solid rgba(37,99,235,0.12);
    }
    .value {
      display: block;
      font-size: clamp(1.8rem, 5vw, 3rem);
      font-weight: 800;
      color: var(--accent);
    }
    .label {
      display: block;
      margin-top: 6px;
      font-size: 0.9rem;
      color: var(--muted);
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }
    .target {
      margin-top: 20px;
      font-size: 0.95rem;
      color: var(--muted);
    }
    .env {
      margin-top: 10px;
      font-size: 0.85rem;
      color: #64748b;
    }
    @media (max-width: 640px) {
      .countdown { grid-template-columns: repeat(2, minmax(0, 1fr)); }
    }
  </style>
</head>
<body>
  <main class="card">
    <h1>Competition starts soon</h1>
    <p>Live countdown to the beginning of the competition.</p>
    <section class="countdown" aria-label="Countdown">
      <div class="unit"><span class="value" id="days">--</span><span class="label">Days</span></div>
      <div class="unit"><span class="value" id="hours">--</span><span class="label">Hours</span></div>
      <div class="unit"><span class="value" id="minutes">--</span><span class="label">Minutes</span></div>
      <div class="unit"><span class="value" id="seconds">--</span><span class="label">Seconds</span></div>
    </section>
    <div class="target">Target start: {{targetDisplay}} (Europe/Prague)</div>
    <div class="env">Environment: {{builder.Environment.EnvironmentName}}</div>
  </main>
  <script>
    const target = new Date("{{target:O}}").getTime();
    const ids = ["days", "hours", "minutes", "seconds"];

    function tick() {
      const now = Date.now();
      const remaining = Math.max(0, target - now);
      const seconds = Math.floor(remaining / 1000);
      const days = Math.floor(seconds / 86400);
      const hours = Math.floor((seconds % 86400) / 3600);
      const minutes = Math.floor((seconds % 3600) / 60);
      const secs = seconds % 60;
      [days, hours, minutes, secs].forEach((value, index) => {
        document.getElementById(ids[index]).textContent = String(value).padStart(2, "0");
      });
      if (remaining === 0) {
        document.querySelector("h1").textContent = "Competition is on!";
        document.querySelector("p").textContent = "The competition has started.";
      }
    }

    tick();
    setInterval(tick, 1000);
  </script>
</body>
</html>
""", "text/html");
});

app.Run();

sealed class CompetitionSettings
{
    public string Title { get; set; } = "Competition Countdown";

    public DateTimeOffset TargetDate { get; set; } = new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.FromHours(2));

    public DateTimeOffset GetTargetDate() => TargetDate;
}
