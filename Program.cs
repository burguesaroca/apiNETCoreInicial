using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NATS.Client;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to read EndPoints from configuration (appsettings.json)
builder.WebHost.ConfigureKestrel((context, options) =>
{
    // Bind the Kestrel configuration section so endpoints defined in appsettings.json are used
    options.Configure(context.Configuration.GetSection("Kestrel"));
});

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Treat JsonElement as open object in OpenAPI (so Swagger UI shows it as JSON/object)
    c.MapType<System.Text.Json.JsonElement>(() => new Microsoft.OpenApi.Models.OpenApiSchema
    {
        Type = "object",
        AdditionalPropertiesAllowed = true
    });
});

// Configure NATS connection from configuration
var natsSection = builder.Configuration.GetSection("Nats");
var natsUrl = natsSection.GetValue<string>("Url") ?? "nats://localhost:4222";
var natsOptions = ConnectionFactory.GetDefaultOptions();
natsOptions.Url = natsUrl;
var connectionFactory = new ConnectionFactory();
IConnection? natsConnection = null;
try
{
    natsConnection = connectionFactory.CreateConnection(natsOptions);
    builder.Services.AddSingleton<IConnection>(natsConnection);
}
catch (Exception ex)
{
    // Do not crash the process if NATS is down; log a console message and continue.
    Console.WriteLine($"WARNING: Could not connect to NATS at '{natsUrl}'. The application will continue without NATS. Error: {ex.Message}");
}

var app = builder.Build();

// Configure the HTTP request pipeline
// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
// }

// Define the POST endpoint
app.MapPost("/api/publisher", (MensajeRequest request, IServiceProvider sp) =>
{
    // Read subject from request (if provided) otherwise from configuration
    var subject = request.Subject ?? builder.Configuration.GetValue<string>("Nats:Subject") ?? "subjectName";

    // Serialize the JSON message to UTF8 bytes and capture result
    // Configure serializer options to avoid escaping characters like '<' (so XML strings stay raw)
    var jsonOptions = new JsonSerializerOptions
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    byte[] payload;
    try
    {
        if (request.Message.ValueKind == JsonValueKind.String)
        {
            // If message is a JSON string (e.g. contains raw XML), publish the raw string bytes
            var raw = request.Message.GetString() ?? string.Empty;
            payload = Encoding.UTF8.GetBytes(raw);
        }
        else
        {
            // Otherwise publish the JSON-serialized bytes using relaxed escaping
            payload = JsonSerializer.SerializeToUtf8Bytes(request.Message, jsonOptions);
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = "Invalid JSON in 'message' field", detail = ex.Message });
    }
    bool published = false;
    string? error = null;

    // Resolve the NATS connection from the service provider. It may be null if the connection
    // failed at startup (we purposely allow the app to run when NATS is down).
    var nats = sp.GetService(typeof(IConnection)) as IConnection;
    if (nats == null)
    {
        // NATS not available; return 503 Service Unavailable with a helpful message
        var body = new
        {
            message = (object?)null,
            subject = subject,
            published = false,
            error = "NATS connection not available"
        };
        return Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    try
    {
        nats.Publish(subject, payload);
        published = true;
    }
    catch (Exception ex)
    {
        published = false;
        error = ex.Message;
    }

    // Return the same JSON object as 'message' in the response
    object? responseMessage = null;
    try
    {
        // Serialize then deserialize using the same relaxed options so the response shows unescaped values
        var str = JsonSerializer.Serialize(request.Message, jsonOptions);
        responseMessage = JsonSerializer.Deserialize<JsonElement>(str);
    }
    catch
    {
        responseMessage = null;
    }

    var response = new
    {
        message = responseMessage,
        subject = subject,
        published = published,
        error = error
    };
    return Results.Ok(response);
});

// Ensure NATS connection is closed when the app stops
app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        if (natsConnection != null)
        {
            natsConnection.Drain();
            natsConnection.Close();
        }
    }
    catch { }
});

app.Run();

public class MensajeRequest
{
    // Message is JSON-typed; use JsonElement to accept arbitrary JSON
    public JsonElement Message { get; set; }
    // Optional subject to override the configured NATS subject
    public string? Subject { get; set; }
}

public class MensajeResponse
{
    public string Response { get; set; } = "";
}