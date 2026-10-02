var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Platform baseline (task T001): auth, tenancy, /health, problem details, rate limiting,
// security headers and SPA hosting are added here following docs/stack/dotnet-react.md.

app.Run();

public partial class Program { }
