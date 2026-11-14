using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.SignalR;
using SkillSwap.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---------- Database ----------
var connStr = cfg.GetConnectionString("Default") ?? "Data Source=skillswap.db";
var isSqlite = connStr.Contains(".db", StringComparison.OrdinalIgnoreCase) ||
               connStr.Contains("Data Source", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (isSqlite)
        o.UseSqlite(connStr);
    else
        o.UseNpgsql(connStr);
});

// ---------- Identity + JWT ----------
builder.Services
    .AddIdentityCore<AppUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.Password.RequireUppercase = false;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;   // keep claim names as written ("sub", "name")
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = cfg["Jwt:Issuer"],
            ValidAudience = cfg["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)),
            NameClaimType = "name"
        };

        // Lets SignalR (added later) pass the JWT as ?access_token= on WebSocket connections.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ---------- Real-time chat ----------
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, SubUserIdProvider>();

// ---------- App services ----------
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<UserMapper>();
builder.Services.AddScoped<InterestService>();
builder.Services.AddScoped<MatchingService>();
builder.Services.AddScoped<SwapMapper>();

// ---------- JSON: enums as strings ----------
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));   // so Swagger shows string enums

// ---------- Swagger ----------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkillSwap API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the token from /api/auth/login (no 'Bearer ' prefix)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});

// ---------- CORS (open for the hackathon; tighten before real use) ----------
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddProblemDetails();

var app = builder.Build();

// Apply pending EF Core migrations or ensure created for SQLite, then seed the skill catalog
// (and demo users only if Seed:DemoUsers is true in appsettings).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (isSqlite)
    {
        await db.Database.EnsureCreatedAsync();
    }
    else
    {
        await db.Database.MigrateAsync();
    }
    await SeedData.RunAsync(scope.ServiceProvider, cfg.GetValue<bool>("Seed:DemoUsers"));
}

app.UseExceptionHandler();
app.UseCors();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapAuth();
app.MapSkills();
app.MapUsers();
app.MapEvents();
app.MapMatches();
app.MapSwaps();
app.MapDiscover();
app.MapSessions();
app.MapNotifications();
app.MapImpact();
app.MapPosts();
app.MapSearch();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();
