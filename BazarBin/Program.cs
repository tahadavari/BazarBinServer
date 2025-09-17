using System.ClientModel;
using System.Text.Json;
using BazarBin.Data;
using BazarBin.Models;
using BazarBin.Options;
using BazarBin.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Npgsql;
using OpenAI;
using OpenAI.Chat;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var importConnectionString = builder.Configuration.GetConnectionString("ImportDatabase")
    ?? throw new InvalidOperationException("Connection string 'ImportDatabase' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(importConnectionString));

builder.Services.Configure<ImportOptions>(builder.Configuration.GetSection("ImportOptions"));
builder.Services.AddScoped<ICsvImportService, CsvImportService>();
builder.Services.AddSingleton<TableSchemaService>();
builder.Services.AddChatClient(sp =>
        new ChatClient(
            "gpt-4o-mini",
            new ApiKeyCredential(builder.Configuration["OpenAiKey"] ?? throw new InvalidOperationException("OpenAiKey is required.")),
            new OpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromSeconds(100)
            })
            .AsIChatClient());

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

var schemaJsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true
};
var tableSchemaSerializationOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    WriteIndented = true
};

app.MapGet("/datasets", async (ApplicationDbContext dbContext, CancellationToken cancellationToken) =>
    {
        var dataSets = await dbContext.DataSets
            .OrderByDescending(dataSet => dataSet.ImportedAt)
            .Select(dataSet => new
            {
                dataSet.Id,
                dataSet.SchemaName,
                dataSet.TableName,
                dataSet.ImportedAt
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(dataSets);
    })
    .WithName("GetDataSets")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Lists imported data sets.";
        operation.Description = "Retrieves the registered import tables with their identifiers.";
        return operation;
    });

app.MapPost("/imports", async (HttpRequest request, ICsvImportService importService, CancellationToken cancellationToken) =>
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Request must be multipart/form-data.");
        }

        var form = await request.ReadFormAsync(cancellationToken);

        if (!form.TryGetValue("schema", out var schemaValues) || schemaValues.Count == 0)
        {
            return Results.BadRequest("Form field 'schema' is required.");
        }

        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return Results.BadRequest("Form file 'file' is required.");
        }

        CsvImportSchema? schema;
        try
        {
            schema = JsonSerializer.Deserialize<CsvImportSchema>(schemaValues[0]!, schemaJsonOptions);
        }
        catch (JsonException ex)
        {
            return Results.BadRequest($"Schema JSON is invalid: {ex.Message}");
        }

        if (schema is null)
        {
            return Results.BadRequest("Schema JSON could not be deserialized.");
        }

        await using var csvStream = file.OpenReadStream();
        var result = await importService.ImportAsync(csvStream, schema, cancellationToken);

        return Results.Ok(result);
    })
    .WithName("ImportCsv")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Creates a PostgreSQL table from a CSV file and imports its rows.";
        operation.Description = "Upload a CSV file alongside a schema definition to build a table in the import schema and bulk load the data.";
        return operation;
    });

app.MapPost("/prompt/{id:int}", async (
        int id,
        PromptRequest request,
        ApplicationDbContext dbContext,
        TableSchemaService tableSchemaService,
        IChatClient chatClient,
        CancellationToken cancellationToken) =>
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
        {
            return Results.BadRequest("Prompt is required.");
        }

        var dataSet = await dbContext.DataSets.FindAsync([id], cancellationToken);
        if (dataSet is null)
        {
            return Results.NotFound();
        }

        TableSchemaResult schemaResult;
        try
        {
            schemaResult = await tableSchemaService.GetTableSchemaAsync(dataSet.SchemaName, dataSet.TableName, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(ex.Message);
        }
        catch (PostgresException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
        }

        var schemaPayload = TableSchemaFormatter.CreateSerializableResponse(schemaResult);
        var schemaJson = JsonSerializer.Serialize(schemaPayload, tableSchemaSerializationOptions);
        var combinedPrompt = $"/*\n{schemaJson}\n*/\n\n{request.Prompt}";

        var response = await chatClient.GetResponseAsync(combinedPrompt, cancellationToken: cancellationToken);
        var responseText = response.ToString();

        return Results.Ok(new PromptResponse(combinedPrompt, responseText));
    })
    .WithName("SendPrompt")
    .WithOpenApi(operation =>
    {
        operation.Summary = "Enrich a prompt with dataset schema details and send it to the AI client.";
        operation.Description = "Loads the stored schema for the specified dataset, prefixes it as a formatted comment, forwards the combined prompt to the configured AI model, and returns the AI response.";
        return operation;
    });

app.Run();

