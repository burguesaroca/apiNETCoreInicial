using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using NATS.Client;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel to read EndPoints from configuration (appsettings.json)
builder.WebHost.ConfigureKestrel((context, options) =>
{
    // Bind the Kestrel configuration section so endpoints defined in appsettings.json are used
    options.Configure(context.Configuration.GetSection("Kestrel"));
});

// Add services to the container
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure NATS connection from configuration
var natsSection = builder.Configuration.GetSection("Nats");
var natsUrl = natsSection.GetValue<string>("Url") ?? "nats://localhost:4222";
var natsOptions = ConnectionFactory.GetDefaultOptions();
natsOptions.Url = natsUrl;
var connectionFactory = new ConnectionFactory();
IConnection natsConnection = connectionFactory.CreateConnection(natsOptions);
builder.Services.AddSingleton(natsConnection);

var app = builder.Build();

// Configure the HTTP request pipeline
// if (app.Environment.IsDevelopment())
// {
    app.UseSwagger();
    app.UseSwaggerUI();
// }

// Define the POST endpoint
app.MapPost("/api/publisher", (MensajeRequest request, IConnection nats) =>
{
    // Read subject from request (if provided) otherwise from configuration
    var subject = request.Subject ?? builder.Configuration.GetValue<string>("Nats:Subject") ?? "microservicio.mensaje";

    // Publish the message as UTF8 and capture result
    var payload = Encoding.UTF8.GetBytes(request.Message ?? string.Empty);
    bool published = false;
    string? error = null;
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

    var response = new
    {
        message = request.Message,
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
        natsConnection?.Drain();
        natsConnection?.Close();
    }
    catch { }
});

app.Run();

public class MensajeRequest
{
    public string Message { get; set; } = "";
    // Optional subject to override the configured NATS subject
    public string? Subject { get; set; }
}

public class MensajeResponse
{
    public string Response { get; set; } = "";
}