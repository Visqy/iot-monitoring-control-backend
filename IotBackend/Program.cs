using System.Text;
using IotBackend.BackgroundServices;
using IotBackend.Infrastructure;
using IotBackend.Options;
using IotBackend.Repositories;
using IotBackend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Scalar.AspNetCore;

if (args is [PasswordHashCommand.CommandName])
{
    Environment.ExitCode = PasswordHashCommand.Run(PasswordHashCommand.ReadPasswordMasked(), Console.Out, Console.Error);
    return;
}

EnvFile.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));

Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

StartupConfigurationValidator.ValidateOrThrow(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var connectionString = builder.Configuration.GetConnectionString("Postgres")!;
builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
builder.Services.AddSingleton<DatabaseInitializer>();

builder.Services.Configure<MqttOptions>(builder.Configuration.GetSection(MqttOptions.SectionName));
builder.Services.AddSingleton<MqttClientService>();
builder.Services.AddSingleton<IMqttPublisher>(sp => sp.GetRequiredService<MqttClientService>());

builder.Services.AddSingleton<RealtimeBroadcaster>();

builder.Services.AddScoped<TelemetryRepository>();
builder.Services.AddScoped<DeviceStateRepository>();
builder.Services.AddScoped<DeviceRepository>();
builder.Services.AddScoped<TelemetryService>();

builder.Services.AddScoped<DeviceService>();

builder.Services.AddScoped<RelayCommandRepository>();
builder.Services.AddScoped<RelayCommandService>();

builder.Services.Configure<RelayOptions>(builder.Configuration.GetSection(RelayOptions.SectionName));
builder.Services.AddHostedService<RelayCommandTimeoutService>();

builder.Services.AddScoped<RfidCardRepository>();
builder.Services.AddScoped<RfidEventRepository>();
builder.Services.AddScoped<RfidService>();

builder.Services.Configure<DeviceOfflineOptions>(builder.Configuration.GetSection(DeviceOfflineOptions.SectionName));
builder.Services.AddHostedService<DeviceOfflineSweepService>();

builder.Services.AddHostedService<MqttSubscriberService>();

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
builder.Services.AddScoped<AuthService>();

var authSigningKey = builder.Configuration["Auth:JwtSigningKey"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authSigningKey))
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await initializer.InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

if (app.Configuration.GetValue<bool?>("HttpsRedirection:Enabled") ?? app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
