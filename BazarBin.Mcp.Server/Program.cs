using BazarBin.Services;

var builder = WebApplication.CreateBuilder(args);


builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();


builder.Services.AddSingleton<TableSchemaService>();
builder.Services.AddHttpClient();

var app = builder.Build();


app.UseHttpsRedirection();


app.Run();