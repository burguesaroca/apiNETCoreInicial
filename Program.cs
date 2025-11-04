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

// Holder for the IConnection so we can set it when available and allow reconnection attempts.
builder.Services.AddSingleton<NatsHolder>();

// Background service will try to establish connection periodically when missing.
builder.Services.AddHostedService<ReconnectNatsService>();

// Note: actual connection attempts happen after the app is built (see below) so the app can
// start immediately even if NATS is down.

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

    // Resolve the NATS holder from the service provider. It may contain null Connection
    // if the connection hasn't been established yet; the background service will try to
    // connect while the app is running.
    var holder = sp.GetService(typeof(NatsHolder)) as NatsHolder;
    var nats = holder?.GetConnection();
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
        // Attempt to gracefully drain/close the NATS connection if it exists in the holder
        var holder = app.Services.GetService(typeof(NatsHolder)) as NatsHolder;
        var existing = holder?.GetConnection();
        if (existing != null)
        {
            existing.Drain();
            existing.Close();
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

// Simple holder for an optional NATS connection so other components can read/update it.
public class NatsHolder
{
    private readonly object _lock = new();
    private IConnection? _connection;

    public IConnection? GetConnection()
    {
        lock (_lock)
        {
            return _connection;
        }
    }

    public void SetConnection(IConnection connection)
    {
        lock (_lock)
        {
            _connection = connection;
        }
    }

    public void ClearConnection()
    {
        lock (_lock)
        {
            _connection = null;
        }
    }
}

// Background service that periodically attempts to create a NATS connection when missing.
public class ReconnectNatsService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IConfiguration _config;
    private readonly NatsHolder _holder;
    private readonly ILogger<ReconnectNatsService> _logger;

    public ReconnectNatsService(IConfiguration config, NatsHolder holder, ILogger<ReconnectNatsService> logger)
    {
        _config = config;
        _holder = holder;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var natsSection = _config.GetSection("Nats");
        var natsUrl = natsSection.GetValue<string>("Url") ?? "nats://localhost:4222";

        var factory = new ConnectionFactory();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_holder.GetConnection() == null)
                {
                    var opts = ConnectionFactory.GetDefaultOptions();
                    opts.Url = natsUrl;
                    // Optional: enable reconnects if desired
                    opts.AllowReconnect = true;

                    _logger.LogInformation("Attempting to connect to NATS at {url}", natsUrl);
                    var conn = factory.CreateConnection(opts);

                    if (conn != null)
                    {
                        _holder.SetConnection(conn);
                        _logger.LogInformation("Connected to NATS at {url}", natsUrl);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not connect to NATS at {url}. Retrying...", natsUrl);
            }

            // Wait before next attempt
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (TaskCanceledException) { }
        }
    }
}