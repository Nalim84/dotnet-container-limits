// Author: Alex Nalim — Senior Backend Software Engineer
// LinkedIn: https://www.linkedin.com/in/alex-nalim/
// Part of the Docker series: demonstrating cgroups in practice
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Container Limits Demo",
        Version = "v1",
        Description = "Demonstrates cgroups in action — with and without resource limits",
        
    });
});

builder.Services.AddHealthChecks();
builder.Logging.AddConsole();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
