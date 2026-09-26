using Dapper;
using Npgsql;
using TestJob.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.WriteIndented = true;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton<TestJobService>();

var app = builder.Build();

await EnsureDatabaseCreatedAsync(app.Services);

app.UseSwagger(options => options.RouteTemplate = "api/swagger/{documentName}/swagger.json");
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/api/swagger/v1/swagger.json", "TestJob API v1");
    options.RoutePrefix = "api/swagger";
});

app.MapControllers();
app.Run();

static async Task EnsureDatabaseCreatedAsync(IServiceProvider services)
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("The connection string 'Default' is not configured.");

    const string createTableSql = """
        CREATE TABLE IF NOT EXISTS elements (
            id BIGSERIAL PRIMARY KEY,
            attribute_value TEXT NOT NULL,
            element_html TEXT NOT NULL
        );
        """;

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var attempt = 0;
    while (true)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cts.Token);
            await connection.ExecuteAsync(createTableSql);
            return;
        }
        catch (Exception) when (attempt++ < 20)
        {
            await Task.Delay(2000, cts.Token);
        }
    }
}