using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using C4GuestPass;
using C4GuestPass.Auth;
using C4GuestPass.C4;
using C4GuestPass.Data;
using C4GuestPass.Domain;
using C4GuestPass.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

// Jako usługa Windows katalogiem roboczym jest System32 – content root ustawiamy na katalog aplikacji.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService()
        ? AppContext.BaseDirectory : default,
});
builder.Host.UseWindowsService(o => o.ServiceName = "C4GuestPass");
var services = builder.Services;

services.Configure<C4Options>(builder.Configuration.GetSection(C4Options.Section));
services.Configure<GuestPassOptions>(builder.Configuration.GetSection(GuestPassOptions.Section));
services.Configure<MailOptions>(builder.Configuration.GetSection(MailOptions.Section));
services.Configure<BootstrapOptions>(builder.Configuration.GetSection(BootstrapOptions.Section));
// Ścieżki względne liczone od katalogu aplikacji, nie od bieżącego katalogu procesu (IIS / usługa / Docker).
var contentRoot = builder.Environment.ContentRootPath;
services.PostConfigure<GuestPassOptions>(o => o.DatabasePath = Path.GetFullPath(o.DatabasePath, contentRoot));
services.PostConfigure<MailOptions>(o => o.PickupDirectory = Path.GetFullPath(o.PickupDirectory, contentRoot));

services.AddSingleton(TimeProvider.System);
services.AddSingleton<Database>();
services.AddSingleton<IVisitStore, SqliteVisitStore>();
services.AddSingleton<ITenancyStore, SqliteTenancyStore>();
services.AddSingleton<AccessCodeGenerator>();
services.AddSingleton<QrRenderer>();
services.AddSingleton<IGuestMailer, GuestMailer>();
services.AddScoped<UserService>();
services.AddScoped<VisitService>();
services.AddHostedService<ProvisioningWorker>();

var appCfg = builder.Configuration.GetSection(GuestPassOptions.Section).Get<GuestPassOptions>() ?? new();

// Za reverse proxy (IIS/nginx/Caddy) z terminacją TLS – właściwy schemat https i IP klienta.
services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// Ochrona przed zgadywaniem haseł: limit prób logowania na IP.
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = appCfg.LoginAttemptsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

var c4Mode = builder.Configuration.GetSection(C4Options.Section).Get<C4Options>()?.Mode ?? C4GatewayMode.Mock;
if (c4Mode == C4GatewayMode.SimpleClient)
{
#if C4SDK
    services.AddSingleton<IC4Gateway, SimpleClientC4Gateway>();
#else
    throw new InvalidOperationException(
        "C4:Mode=SimpleClient wymaga kompilacji z SDK: dotnet build -p:C4SdkVersion=2024 (lub 2026).");
#endif
}
else
{
    services.AddSingleton<MockC4Gateway>();
    services.AddSingleton<IC4Gateway>(sp => sp.GetRequiredService<MockC4Gateway>());
}

services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "c4gp";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = appCfg.SecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.LoginPath = "/login.html";
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.StatusCode = 401;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        // Każde żądanie: konto nadal aktywne, firma aktywna, hasło/rola niezmienione (security stamp)?
        o.Events.OnValidatePrincipal = async ctx =>
        {
            var users = ctx.HttpContext.RequestServices.GetRequiredService<UserService>();
            var me = await users.ResolveAsync(ctx.Principal!, ctx.HttpContext.RequestAborted);
            if (me is null) { ctx.RejectPrincipal(); await ctx.HttpContext.SignOutAsync(); return; }
            ctx.HttpContext.Items[typeof(CurrentUser)] = me;
        };
    });
services.AddAuthorization();
services.AddProblemDetails();
services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<UserService>().BootstrapAsync(CancellationToken.None);

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Ochrona CSRF dla API: wymagany nagłówek, którego formularz z obcej strony nie ustawi (+ SameSite=Strict).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(ctx.Request.Method)
        && ctx.Request.Headers["X-Requested-With"] != "fetch")
    {
        ctx.Response.StatusCode = 400;
        return;
    }
    await next();
});

// Wymuszona zmiana hasła tymczasowego: do tego czasu dostępne tylko /api/me*, /api/logout.
app.Use(async (ctx, next) =>
{
    if (ctx.Items[typeof(CurrentUser)] is CurrentUser me && ctx.Request.Path.StartsWithSegments("/api")
        && !ctx.Request.Path.StartsWithSegments("/api/me") && !ctx.Request.Path.StartsWithSegments("/api/logout"))
    {
        var u = await ctx.RequestServices.GetRequiredService<ITenancyStore>().GetUserAsync(me.Id);
        if (u?.MustChangePassword == true)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsJsonAsync(new { error = "Zmień hasło tymczasowe.", code = "must_change_password" });
            return;
        }
    }
    await next();
});

// Mapowanie wyjątków domenowych na odpowiedzi HTTP.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ValidationException ex) when (!ctx.Response.HasStarted)
    {
        ctx.Response.StatusCode = 400;
        await ctx.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
    catch (UnauthorizedAccessException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 403; }
    catch (KeyNotFoundException) when (!ctx.Response.HasStarted) { ctx.Response.StatusCode = 404; }
});

var api = app.MapGroup("/api");
static CurrentUser Me(HttpContext ctx) => (CurrentUser)ctx.Items[typeof(CurrentUser)]!;

// ---------- sesja ----------

api.MapPost("/login", async (LoginRequest req, UserService users, HttpContext ctx, CancellationToken ct) =>
{
    var u = await users.ValidateCredentialsAsync(req.Login ?? "", req.Password ?? "", ct);
    if (u is null)
    {
        await Task.Delay(400, ct);  // spowolnienie zgadywania haseł
        return Results.Unauthorized();
    }
    await ctx.SignInAsync(UserService.ToPrincipal(u, CookieAuthenticationDefaults.AuthenticationScheme));
    return Results.Ok(new { u.Login, u.MustChangePassword });
}).RequireRateLimiting("login");

api.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync();
    return Results.Ok();
});

var secured = api.MapGroup("").RequireAuthorization();

secured.MapGet("/me", async (HttpContext ctx, ITenancyStore store, IOptions<GuestPassOptions> o, CancellationToken ct) =>
{
    var me = Me(ctx);
    var u = (await store.GetUserAsync(me.Id, ct))!;
    var company = me.CompanyId is { } cid ? await store.GetCompanyAsync(cid, ct) : null;
    return new
    {
        me.Id, me.Login, me.DisplayName, Role = me.Role.ToString(), me.CompanyId, CompanyName = company?.Name,
        u.MustChangePassword, o.Value.SiteName, me.CanManageUsers, me.IsBuildingAdmin,
    };
});

secured.MapPost("/me/password", async (ChangePasswordRequest r, HttpContext ctx, UserService users, CancellationToken ct) =>
{
    await users.ChangeOwnPasswordAsync(Me(ctx), r.CurrentPassword ?? "", r.NewPassword ?? "", ct);
    // nowe ciasteczko z nowym security stamp
    var u = (await ctx.RequestServices.GetRequiredService<ITenancyStore>().GetUserAsync(Me(ctx).Id, ct))!;
    await ctx.SignInAsync(UserService.ToPrincipal(u, CookieAuthenticationDefaults.AuthenticationScheme));
    return Results.Ok();
});

// ---------- słowniki ----------

secured.MapGet("/zones", (IOptions<C4Options> o) =>
    o.Value.AccessProfiles.Select(p => new { p.Id, p.Name, p.Description }));

secured.MapGet("/companies", async (HttpContext ctx, UserService users, CancellationToken ct) =>
    (await users.ListCompaniesAsync(Me(ctx), ct)).Select(CompanyDto.From));

secured.MapPost("/companies", async (CompanyRequest r, HttpContext ctx, UserService users, CancellationToken ct) =>
    CompanyDto.From(await users.SaveCompanyAsync(Me(ctx), null, r, ct)));

secured.MapPut("/companies/{id:guid}", async (Guid id, CompanyRequest r, HttpContext ctx, UserService users, CancellationToken ct) =>
    CompanyDto.From(await users.SaveCompanyAsync(Me(ctx), id, r, ct)));

secured.MapGet("/health", async (IC4Gateway c4, CancellationToken ct) => await c4.CheckAsync(ct));

// ---------- użytkownicy ----------

secured.MapGet("/users", async (HttpContext ctx, UserService users, CancellationToken ct) =>
    (await users.ListUsersAsync(Me(ctx), ct)).Select(UserDto.From));

secured.MapPost("/users", async (CreateUserRequest r, HttpContext ctx, UserService users, CancellationToken ct) =>
{
    var (u, temp) = await users.CreateUserAsync(Me(ctx), r, ct);
    return Results.Ok(new { User = UserDto.From(u), TempPassword = temp });
});

secured.MapPut("/users/{id:guid}", async (Guid id, UpdateUserRequest r, HttpContext ctx, UserService users, CancellationToken ct) =>
    UserDto.From(await users.UpdateUserAsync(Me(ctx), id, r, ct)));

secured.MapPost("/users/{id:guid}/reset-password", async (Guid id, HttpContext ctx, UserService users, CancellationToken ct) =>
    new { TempPassword = await users.ResetPasswordAsync(Me(ctx), id, ct) });

// ---------- wizyty ----------

secured.MapGet("/visits", async (HttpContext ctx, VisitService svc, CancellationToken ct) =>
    (await svc.ListAsync(Me(ctx), ct)).Select(VisitDto.From));

secured.MapPost("/visits", async (CreateVisitRequest req, HttpContext ctx, VisitService svc, CancellationToken ct) =>
{
    var v = await svc.CreateAsync(req, Me(ctx), ct);
    return Results.Created($"/api/visits/{v.Id}", VisitDto.From(v));
});

secured.MapPost("/visits/{id:guid}/{action:regex(^(resend|revoke|checkin|checkout)$)}",
    async (Guid id, string action, HttpContext ctx, VisitService svc, CancellationToken ct) =>
{
    var v = await svc.GetAsync(Me(ctx), id, ct);
    switch (action)
    {
        case "resend":
            if (v.Status is not (VisitStatus.Scheduled or VisitStatus.Active)) throw new ValidationException("Wizyta zakończona.");
            await svc.SendMailAsync(v, ct); break;
        case "revoke": await svc.RevokeAsync(v, ct); break;
        case "checkin": await svc.CheckInAsync(v, ct); break;
        case "checkout": await svc.CheckOutAsync(v, ct); break;
    }
    return Results.Ok(VisitDto.From(v));
});

secured.MapGet("/visits/{id:guid}/qr.png", async (Guid id, HttpContext ctx, VisitService svc, QrRenderer qr, CancellationToken ct) =>
    Results.File(qr.Png((await svc.GetAsync(Me(ctx), id, ct)).AccessCode), "image/png"));

app.Run();

public sealed record LoginRequest(string? Login, string? Password);
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record CompanyDto(Guid Id, string Name, bool Active, int MaxConcurrentGuests, List<CompanyZone> Zones)
{
    public static CompanyDto From(Company c) => new(c.Id, c.Name, c.Active, c.MaxConcurrentGuests, c.Zones);
}

public sealed record UserDto(Guid Id, string Login, string DisplayName, string? Email, string Role, Guid? CompanyId,
    bool Active, bool MustChangePassword, DateTimeOffset? LastLoginAt)
{
    public static UserDto From(AppUser u) => new(u.Id, u.Login, u.DisplayName, u.Email, u.Role.ToString(), u.CompanyId,
        u.Active, u.MustChangePassword, u.LastLoginAt);
}

public sealed record VisitDto(
    Guid Id, Guid CompanyId, string FirstName, string LastName, string Email, string? Phone, string? Company, string? HostName,
    string AccessProfileId, DateTimeOffset ValidFrom, DateTimeOffset ValidTo, string Status, string AccessCodeMasked,
    bool EmailSent, DateTimeOffset? CheckedInAt, DateTimeOffset? CheckedOutAt, string? LastError, string CreatedBy, DateTimeOffset CreatedAt)
{
    public static VisitDto From(Visit v) => new(
        v.Id, v.CompanyId, v.FirstName, v.LastName, v.Email, v.Phone, v.Company, v.HostName, v.AccessProfileId,
        v.ValidFrom, v.ValidTo, v.Status.ToString(),
        new string('•', Math.Max(0, v.AccessCode.Length - 4)) + v.AccessCode[^4..],
        v.EmailSentAt is not null, v.CheckedInAt, v.CheckedOutAt, v.LastError, v.CreatedBy, v.CreatedAt);
}

public partial class Program;
