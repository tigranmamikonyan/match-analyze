using MatchAnalyzer.Api.Data;
using MatchAnalyzer.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient();
builder.Services.AddScoped<MatchParserService>();
builder.Services.AddScoped<MatchAnalysisService>();
builder.Services.AddHostedService<MatchAnalyzer.Api.Background.MatchSyncBackgroundService>();

var app = builder.Build();

await using var scope = app.Services.CreateAsyncScope();
using var db = scope.ServiceProvider.GetService<AppDbContext>();
await db.Database.MigrateAsync();

app.UseCors(x =>
{
    x.SetIsOriginAllowed(_ => true);
    x.AllowAnyMethod();
    x.AllowAnyHeader();
    x.AllowCredentials();
    x.WithExposedHeaders("Content-Disposition");
});


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
