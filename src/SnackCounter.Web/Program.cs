using System.Text.Json;

namespace SnackCounter.Web;

// A small web UI for the simulator. Two pages:
//   http://localhost:5080/            test console: see and control everything
//   http://localhost:5080/person.html one person's chat, like the real chat app would look
// Run: dotnet run --project src/SnackCounter.Web
public class Program
{
    private static SimulatorHost _host = null!;

    public static void Main(string[] args)
    {
        _host = new SimulatorHost(FindConfigFolder());

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.ConfigureHttpJsonOptions(UseFields);
        builder.WebHost.UseUrls("http://localhost:5080");
        WebApplication app = builder.Build();

        app.UseDefaultFiles();
        // Never let the browser keep an old copy of the pages, so a refresh always shows the latest version.
        StaticFileOptions noCache = new StaticFileOptions();
        noCache.OnPrepareResponse = NoCache;
        app.UseStaticFiles(noCache);
        app.MapGet("/api/state", GetState);
        app.MapPost("/api/send", Send);

        Console.WriteLine("Test console:  http://localhost:5080/");
        Console.WriteLine("Person chat:   http://localhost:5080/person.html");
        app.Run();
    }

    private static void NoCache(Microsoft.AspNetCore.StaticFiles.StaticFileResponseContext context)
    {
        context.Context.Response.Headers["Cache-Control"] = "no-store";
    }

    private static IResult GetState()
    {
        return Results.Json(_host.GetState());
    }

    private static IResult Send(SendRequest request)
    {
        _host.Send(request.Line ?? "");
        return Results.Json(_host.GetState());
    }

    // Our model classes use public fields, so tell the JSON writer to include them.
    private static void UseFields(Microsoft.AspNetCore.Http.Json.JsonOptions options)
    {
        options.SerializerOptions.IncludeFields = true;
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    }

    // Look for the "config" folder here or in a parent folder, so it works from the repo root or the project folder.
    private static string FindConfigFolder()
    {
        DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "config");
            if (File.Exists(Path.Combine(candidate, "settings.json"))) return candidate;
            dir = dir.Parent;
        }
        throw new Exception("Could not find config/settings.json");
    }
}

// The body of POST /api/send: { "line": "anna: yes" }
public class SendRequest
{
    public string? Line { get; set; }
}
