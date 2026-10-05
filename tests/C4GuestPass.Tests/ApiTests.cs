using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace C4GuestPass.Tests;

public sealed class ApiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "c4gp-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;
    private const string AdminPwd = "Admin12345!";

    public ApiTests()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("GuestPass:DatabasePath", Path.Combine(_dir, "t.db"));
            b.UseSetting("Mail:PickupDirectory", Path.Combine(_dir, "mail"));
            b.UseSetting("Bootstrap:AdminPassword", AdminPwd);
        });
    }

    public void Dispose() { _factory.Dispose(); try { Directory.Delete(_dir, true); } catch { } }

    private HttpClient Client()
    {
        var c = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        c.DefaultRequestHeaders.Add("X-Requested-With", "fetch");
        return c;
    }

    private async Task<HttpClient> Login(string login, string pwd)
    {
        var c = Client();
        (await c.PostAsJsonAsync("/api/login", new { login, password = pwd })).EnsureSuccessStatusCode();
        return c;
    }

    [Fact]
    public async Task Api_requires_login_and_csrf_header()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Client().GetAsync("/api/visits")).StatusCode);
        var raw = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await raw.PostAsJsonAsync("/api/login", new { login = "admin", password = AdminPwd })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await Client().PostAsJsonAsync("/api/login", new { login = "admin", password = "zle" })).StatusCode);
    }

    [Fact]
    public async Task Tenant_flow_admin_creates_company_and_user_user_invites_guest()
    {
        var admin = await Login("admin", AdminPwd);

        // firma ze strefą lobby
        var co = await (await admin.PostAsJsonAsync("/api/companies", new
        {
            name = "ACME", active = true, maxConcurrentGuests = 10, zones = new[] { new { profileId = "lobby" } }
        })).Content.ReadFromJsonAsync<JsonElement>();
        var coId = co.GetProperty("id").GetGuid();

        // konto firmowe (CompanyAdmin) z hasłem tymczasowym
        var created = await (await admin.PostAsJsonAsync("/api/users", new
        {
            login = "jan.acme", displayName = "Jan z ACME", role = "CompanyAdmin", companyId = coId
        })).Content.ReadFromJsonAsync<JsonElement>();
        var temp = created.GetProperty("tempPassword").GetString()!;

        var jan = await Login("jan.acme", temp);
        // hasło tymczasowe -> blokada API do zmiany
        Assert.Equal(HttpStatusCode.Forbidden, (await jan.GetAsync("/api/visits")).StatusCode);
        (await jan.PostAsJsonAsync("/api/me/password", new { currentPassword = temp, newPassword = "NoweHaslo123" })).EnsureSuccessStatusCode();

        // firma nie może nadać strefy, której nie ma
        var now = DateTimeOffset.UtcNow;
        var denied = await jan.PostAsJsonAsync("/api/visits", new
        {
            firstName = "Ewa", lastName = "Test", email = "ewa@example.com", accessProfileId = "floor3", validFrom = now, validTo = now.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

        var ok = await jan.PostAsJsonAsync("/api/visits", new
        {
            firstName = "Ewa", lastName = "Test", email = "ewa@example.com", accessProfileId = "lobby", validFrom = now, validTo = now.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var v = await ok.Content.ReadFromJsonAsync<VisitDto>();
        Assert.Equal("Active", v!.Status);
        Assert.Equal(coId, v.CompanyId);
        Assert.True(v.EmailSent);
        Assert.Equal(0x89, (await jan.GetByteArrayAsync($"/api/visits/{v.Id}/qr.png"))[0]);

        // CompanyAdmin nie może tworzyć admina budynku ani firm
        Assert.Equal(HttpStatusCode.Forbidden, (await jan.PostAsJsonAsync("/api/users", new { login = "x123", displayName = "X", role = "BuildingAdmin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await jan.PostAsJsonAsync("/api/companies", new { name = "Z", active = true, maxConcurrentGuests = 0, zones = new[] { new { profileId = "lobby" } } })).StatusCode);

        // check-in / check-out
        (await jan.PostAsync($"/api/visits/{v.Id}/checkin", null)).EnsureSuccessStatusCode();
        var outV = await (await jan.PostAsync($"/api/visits/{v.Id}/checkout", null)).Content.ReadFromJsonAsync<VisitDto>();
        Assert.Equal("Expired", outV!.Status);

        // blokada firmy wylogowuje jej użytkowników natychmiast
        (await admin.PutAsJsonAsync($"/api/companies/{coId}", new { name = "ACME", active = false, maxConcurrentGuests = 10, zones = new[] { new { profileId = "lobby" } } })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await jan.GetAsync("/api/visits")).StatusCode);

        // admin widzi wizytę firmy
        var all = await admin.GetFromJsonAsync<List<VisitDto>>("/api/visits");
        Assert.Single(all!);
    }
}
