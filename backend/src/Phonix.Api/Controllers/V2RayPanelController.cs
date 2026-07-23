using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// What the panel is told about a configured V2Ray panel. The password is absent BY TYPE — only whether one
// is stored is exposed — so no endpoint can leak it by forgetting to strip it.
public sealed record V2RayPanelDto(
    int Id, V2RayProvider Provider, string Url, string Username, bool HasPassword, bool Enabled,
    string CreatedAtUtc, string LastCheckAtUtc, bool LastCheckOk, string LastCheckError, int InboundCount,
    bool HasApiToken);

// Either an API token (preferred — it skips the panel's CSRF/session handshake entirely) or a
// username/password pair is enough to connect.
public sealed record V2RayPanelInput(V2RayProvider Provider, string Url, string Username, string Password, string? ApiToken);

// Create an account on a stored panel. Email is the label the account is created under; the three limits
// follow the panel's own "0 = unlimited" convention (0 GB, 0 IPs, 0 days = no expiry). InboundIds are the
// specific inbounds/locations to create on (a plan's mapping); empty falls back to all enabled.
public sealed record V2RayNewClientInput(string Email, long TotalGb, int LimitIp, int DurationDays, int[]? InboundIds);

// A provider offered in the "add panel" wizard, and whether its connector is actually wired up yet.
public sealed record V2RayProviderDto(V2RayProvider Provider, string Name, bool Available);

// The owner-only V2Ray control surface: it holds the credentials the shop uses to sign in to Xray panels
// and (later) create the accounts customers buy. Gated to the owner ALONE — a normal Admin authenticates
// but is refused — because a panel credential grants standing control of live VPN infrastructure.
[ApiController]
[Route("api/v2ray")]
[Authorize(Roles = nameof(UserRole.Admin))]
[OwnerOnly]
public class V2RayPanelController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IV2RayPanelConnector _connector;

    public V2RayPanelController(IDataStore store, IV2RayPanelConnector connector)
    {
        _store = store;
        _connector = connector;
    }

    private IActionResult Problem(string? error) => BadRequest(error ?? "عملیات ناموفق بود.");

    private static V2RayPanelDto ToDto(V2RayPanel p) => new(
        p.Id, p.Provider, p.Url, p.Username, !string.IsNullOrEmpty(p.Password), p.Enabled,
        p.CreatedAtUtc, p.LastCheckAtUtc, p.LastCheckOk, p.LastCheckError, p.InboundCount,
        !string.IsNullOrEmpty(p.ApiToken));

    private static V2RayCredentials Creds(V2RayPanel p) => new(p.Url, p.Username, p.Password, p.ApiToken);

    [HttpGet("providers")]
    public IReadOnlyList<V2RayProviderDto> Providers() => new[]
    {
        new V2RayProviderDto(V2RayProvider.Sanaei, "سنایی (3x-ui)", true),
        new V2RayProviderDto(V2RayProvider.Pasargad, "پاسارگاد", false),
        new V2RayProviderDto(V2RayProvider.Marzban, "مرزبان", false),
        new V2RayProviderDto(V2RayProvider.Alireza, "علیرضا (x-ui)", false),
    };

    [HttpGet("panels")]
    public IReadOnlyList<V2RayPanelDto> List() => _store.GetV2RayPanels().Select(ToDto).ToList();

    // Verify a URL + credentials WITHOUT saving — the wizard's "login / test" button. Lets the owner confirm
    // the panel answers before committing anything to the store.
    [HttpPost("test")]
    public async Task<IActionResult> Test(V2RayPanelInput input, CancellationToken ct)
    {
        var result = await _connector.TestAsync(input.Provider, new V2RayCredentials(input.Url, input.Username, input.Password, input.ApiToken ?? ""), ct);
        return result.Ok
            ? Ok(new { ok = true, inboundCount = result.InboundCount })
            : Problem(result.Error);
    }

    // Add a panel. The connection is verified first: a panel that cannot be reached is a configuration
    // mistake, and storing it silently would only surface later as a failed order. Saving succeeds only once
    // the login and inbound read both pass.
    [HttpPost("panels")]
    public async Task<IActionResult> Add(V2RayPanelInput input, CancellationToken ct)
    {
        var url = IV2RayPanelConnector.NormalizeUrl(input.Url);
        if (url is null) return Problem("آدرس پنل معتبر نیست. نمونه: https://sub.example.com:8080/webpath");
        var hasToken = !string.IsNullOrWhiteSpace(input.ApiToken);
        if (!hasToken && (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password)))
            return Problem("توکن API یا نام کاربری و گذرواژه پنل را وارد کنید.");

        var test = await _connector.TestAsync(input.Provider, new V2RayCredentials(url, input.Username, input.Password, input.ApiToken ?? ""), ct);
        if (!test.Ok) return Problem(test.Error);

        var saved = _store.AddV2RayPanel(new V2RayPanel
        {
            Provider = input.Provider,
            Url = url,
            Username = (input.Username ?? "").Trim(),
            Password = input.Password ?? "",
            ApiToken = (input.ApiToken ?? "").Trim(),
            Enabled = true,
            LastCheckAtUtc = DateTime.UtcNow.ToString("O"),
            LastCheckOk = true,
            InboundCount = test.InboundCount,
        });
        return Ok(ToDto(saved));
    }

    // The inbounds/locations of a stored panel — a live read after a real login, so it both proves the
    // connection and gives the operator the list a service will later be mapped onto.
    [HttpGet("panels/{id:int}/inbounds")]
    public async Task<IActionResult> Inbounds(int id, CancellationToken ct)
    {
        var panel = _store.GetV2RayPanel(id);
        if (panel is null) return NotFound();

        var result = await _connector.ListInboundsAsync(panel.Provider, Creds(panel), ct);
        return result.Ok ? Ok(result.Inbounds) : Problem(result.Error);
    }

    // Re-test a stored panel and record the outcome so the list shows a fresh status.
    [HttpPost("panels/{id:int}/test")]
    public async Task<IActionResult> TestStored(int id, CancellationToken ct)
    {
        var panel = _store.GetV2RayPanel(id);
        if (panel is null) return NotFound();

        var result = await _connector.TestAsync(panel.Provider, Creds(panel), ct);
        _store.RecordV2RayPanelCheck(id, result.Ok, result.Error ?? "", result.InboundCount);
        return result.Ok
            ? Ok(new { ok = true, inboundCount = result.InboundCount })
            : Problem(result.Error);
    }

    // Create an account on a stored panel: the shop logs in, then adds the client to every enabled inbound
    // ("select all"), all sharing one UUID and one subscription id. This is the call order fulfilment will
    // make; exposed to the owner now so the flow can be exercised before the purchase wiring is in place.
    [HttpPost("panels/{id:int}/client")]
    public async Task<IActionResult> AddClient(int id, V2RayNewClientInput input, CancellationToken ct)
    {
        var panel = _store.GetV2RayPanel(id);
        if (panel is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Email)) return Problem("نام (Email) اکانت را وارد کنید.");

        var result = await _connector.AddClientAsync(
            panel.Provider, Creds(panel),
            new V2RayNewClient(input.Email.Trim(), input.TotalGb, input.LimitIp, input.DurationDays),
            input.InboundIds ?? Array.Empty<int>(),
            ct);

        return result.Ok
            ? Ok(new { ok = true, uuid = result.Uuid, subId = result.SubId, inboundsAdded = result.InboundsAdded })
            : Problem(result.Error);
    }

    [HttpDelete("panels/{id:int}")]
    public IActionResult Delete(int id) =>
        _store.DeleteV2RayPanel(id) ? Ok(new { ok = true }) : NotFound();
}
