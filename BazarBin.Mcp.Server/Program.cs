using System.IO;
using Asp.Versioning;
using Asp.Versioning.Builder;
using BazarBin.Application.Common;
using BazarBin.Application.Prompts;
using BazarBin.Application.Prompts.Contracts;
using BazarBin.Infrastructure;
using BazarBin.Mcp.Server.Contracts;
using DotNetEnv;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var localEnvPath = Path.Combine(AppContext.BaseDirectory, ".env");
if (File.Exists(localEnvPath))
{
    Env.Load(localEnvPath);
}
else if (File.Exists(".env"))
{
    Env.Load();
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

builder.Services.AddProblemDetails();
builder.Services.AddBazarBinInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler(appBuilder =>
{
    appBuilder.Run(async context =>
    {
        var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = exceptionFeature?.Error;

        ProblemDetails problem = exception switch
        {
            AppException appException => new ProblemDetails
            {
                Status = (int)appException.StatusCode,
                Title = "Request processing failed.",
                Detail = appException.Message,
                Extensions = { ["errorCode"] = appException.ErrorCode }
            },
            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected server error.",
                Detail = "An unexpected error occurred while processing the request."
            }
        };

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

ApiVersionSet versionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(1, 0))
    .ReportApiVersions()
    .Build();

var promptGroup = app.MapGroup("/api/v{version:apiVersion}/prompt")
    .WithApiVersionSet(versionSet)
    .HasApiVersion(1.0)
    .WithTags("Prompt");

promptGroup.MapGet("/{datasetId}", async Task<IResult> (
        string datasetId,
        GetPromptResponseHandler handler,
        CancellationToken cancellationToken) =>
    {
        var result = await handler.HandleAsync(new GetPromptResponseRequest(datasetId), cancellationToken).ConfigureAwait(false);
        var response = new PromptResponseDto(
            result.DatasetId,
            result.OriginalPrompt,
            result.SchemaComment,
            result.FinalPrompt,
            result.AiResponse);
        return TypedResults.Ok(response);
    })
    .Produces<PromptResponseDto>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .WithName("GetPromptByDatasetId")
    .WithOpenApi();

app.Run();

public partial class Program;
