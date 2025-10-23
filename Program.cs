using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

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

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Define the POST endpoint
app.MapPost("/api/mensaje", (MensajeRequest request) =>
{
    var response = new MensajeResponse
    {
        Response = request.Mensaje.ToUpper()
    };
    return Results.Ok(response);
});

app.Run();

public class MensajeRequest
{
    public string Mensaje { get; set; } = "";
}

public class MensajeResponse
{
    public string Response { get; set; } = "";
}