
using Serilog;
using Downloader_Backend.Logic;
using Downloader_Backend.Model;
using Downloader_Backend.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.InteropServices;


var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});


// --------------------
// SERVICES
// --------------------
builder.Services.AddControllers();

// SINGLETON - Shared state across all requests
builder.Services.AddSingleton<DownloadTracker>();
builder.Services.AddSingleton<PortKiller>();

// SCOPED - New instance per request
builder.Services.AddScoped<IDownloadPersistence, DownloadPersistence>();

// Other scoped services
builder.Services.AddTransient<Utility>();
builder.Services.AddTransient<ProcessControl>();
builder.Services.AddTransient<DownloaderController>();

// HOSTED SERVICE - Singleton by default, runs once for app lifetime
builder.Services.AddHostedService<FileCleanupService>();


// --------------------
// DATABASE
// --------------------
var databasePath = Utility.Create_Path(Making_Logs_Path: false);

builder.Services.AddDbContextFactory<DownloadContext>(options =>
{
    options.UseSqlite($"Data Source={databasePath};Cache=Shared");
});

/* 
// OPTIONAL — only if controllers need scoped DbContext injection.
// We dont need this as Task.Run thread get disposed with DB connection after download is Done so we cant update DB due to connection is disposed. we need to reopen it again.
builder.Services.AddDbContext<DownloadContext>(options =>
{
    options.UseSqlite($"Data Source={databasePath};Cache=Shared");
}); */


// --------------------
// KESTREL CONFIG
// --------------------
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5050);
});



/* builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
    builder => builder.WithOrigins("http://localhost:5173")
    .AllowAnyMethod()
    .AllowAnyHeader());
}); */



/* 
// Enable HTTPS with Let's Encrypt certificate
// Note: This is a placeholder for your actual certificate path and password.

// Build cert path
var certFolder = Path.Combine(AppContext.BaseDirectory, "Cert");
var certPath = Path.Combine(certFolder, "mediadownloader.pfx");
var certPassword = "ARehman988";

// ✅ Ensure the Cert folder exists
if (!Directory.Exists(certFolder))
{
    Directory.CreateDirectory(certFolder);
    Console.WriteLine($"[INFO] Created missing folder: {certFolder}");
}

// ✅ Check if cert file exists
if (!File.Exists(certPath))
{
    Console.WriteLine($"[ERROR] Certificate file not found at: {certPath}");
    throw new FileNotFoundException("Certificate file not found. Please ensure the certificate is present in the Cert folder. " + certPath);
}

builder.WebHost.ConfigureKestrel(opts =>
{
    // Redirect HTTP to HTTPS (port 80)
    opts.ListenAnyIP(80);

    // HTTPS using Let's Encrypt cert
    opts.ListenAnyIP(443, lo =>
    {
        lo.UseHttps(certPath, certPassword);
    });
}); */

// --------------------
// SERILOG CONFIG (ONLY PIPELINE)
// --------------------
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: Path.Combine(Utility.Create_Path(Making_Logs_Path: true), "log-.txt"), // dynamic path for logs.
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 10, // keep last 10 logs
        fileSizeLimitBytes: 10_000_000, // 10 MB
        rollOnFileSizeLimit: true,
        shared: true
    )
    .CreateLogger();


// Plug Serilog into ASP.NET Core
builder.Host.UseSerilog();


var app = builder.Build();

// --------------------
// DB INIT
// --------------------
Utility utility;
PortKiller Port_Killer;

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DownloadContext>();
    Port_Killer = scope.ServiceProvider.GetRequiredService<PortKiller>();
    var history = scope.ServiceProvider.GetRequiredService<IDownloadPersistence>();
    utility = scope.ServiceProvider.GetRequiredService<Utility>();

    try
    {
        dbContext.Database.EnsureCreated();
        Log.Information("Database initialized successfully.");
    }
    catch (Exception ex)
    {
        Log.Fatal(ex, "Failed to initialize database");
    }

    await history.GetAllJobsAsync_From_DB();
    //
}



// --------------------
// HTTP PIPELINE
// --------------------
// app.UseHttpsRedirection();
// app.UseCors("AllowAllOrigins");
app.UseStaticFiles();
app.MapControllers();
app.MapGet("/", () => "Media Downloader Home!");
app.MapFallbackToFile("index.html");



/* 
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true
 */


// --------------------
// START
// --------------------
const int Port = 5050;

try
{
        Log.Information("---- App Started ----");

        if (Port_Killer.Is_Our_Backend_Running(Port))
        {
            // OUR backend (service or previous instance) is already running
            utility.OpenBrowser($"http://localhost:{Port}/index.html");
            Log.Information("Our backend is already running → opened browser and exiting this instance.");
            return; // exit cleanly — do not start another server
        }

        // Either port is free OR occupied by external process → make it ours
        Log.Information("Port {Port} is free or used by external process. Preparing our backend...", Port);

        if (!Port_Killer.EnsurePortAvailable(Port))
        {
            Log.Error("Could not free port {Port}. Exiting...", Port);
            return;
        }

        // First-time setup: enable systemd user service (Linux only)
        utility.Checking_And_Starting_Linux_Service();
        //
        utility.OpenBrowser($"http://localhost:{Port}/index.html");
        await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application crashed");
}
finally
{
    Log.CloseAndFlush();
}


