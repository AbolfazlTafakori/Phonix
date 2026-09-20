using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Security;
using Phonix.Api.Services;

namespace Phonix.Api.Controllers;

// What the panel is told about a configured W-UI panel. The password and token are absent BY TYPE — only
// whether one is stored is exposed — so no endpoint can leak them by forgetting to strip them.
public sealed record WireGuardPanelDto(
    int Id, WireGuardProvider Provider, string Url, string Username, bool HasPassword, bool HasApiToken, bool Enabled,
    string CreatedAtUtc, string LastCheckAtUtc, bool LastCheckOk, string LastCheckError, int InterfaceCount, string PanelVersion,
    string Name, string Remark, string Flag, int Capacity);

// Either an access token (preferred — W-UI means it for machines, and it skips the cookie-bound browser
// session) or a username/password pair is enough to connect.
public sealed record WireGuardPanelInput(
    WireGuardProvider Provider, string Url, string Username, string Password, string? ApiToken,
    string? Name, string? Remark, string? Flag, int Capacity);

// Create a customer on a stored panel. Name is the label the customer is created under; traffic and
// duration follow W-UI's "0 = unlimited" convention; the device limit is at least 1. InterfaceIds are the
// specific tunnels to place the customer on (a plan's mapping).
public sealed record WireGuardNewClientInput(string Name, long TotalGb, int DeviceLimit, int DurationDays, bool StartOnFirstUse, int[]? InterfaceIds);

// A provider offered in the "add panel" wizard, and whether its connector is actually wired up.
public sealed record WireGuardProviderDto(WireGuardProvider Provider, string Name, bool Available);

// The owner-only WireGuard control surface: it holds the credentials the shop uses to talk to W-UI panels
// and create the customers buyers pay for. Gated to the owner ALONE — a normal Admin authenticates but is
// refused — because a panel credential grants standing control of live VPN infrastructure.
[ApiController]
[Route("api/wireguard")]
[Authorize(Roles = nameof(UserRole.Admin))]
[OwnerOnly]
public class WireGuardPanelController : ControllerBase
{
    private readonly IDataStore _store;
    private readonly IWireGuardPanelConnector _connector;

    public WireGuardPanelController(IDataStore store, IWireGuardPanelConnector connector)
    {
        _store = store;
        _connector = connector;
    }

    private const string UrlHint = "آدرس پنل معتبر نیست. نمونه: https://1.2.3.4:41873/webBasePath";

    private IActionResult Problem(string? error) => BadRequest(error ?? "عملیات ناموفق بود.");

    private static WireGuardPanelDto ToDto(WireGuardPanel p) => new(
        p.Id, p.Provider, p.Url, p.Username, !string.IsNullOrEmpty(p.Password), !string.IsNullOrEmpty(p.ApiToken), p.Enabled,
        p.CreatedAtUtc, p.LastCheckAtUtc, p.LastCheckOk, p.LastCheckError, p.InterfaceCount, p.PanelVersion,
        p.Name, p.Remark, p.Flag, p.Capacity);

    private static WireGuardCredentials Creds(WireGuardPanel p) => new(p.Url, p.Username, p.Password, p.ApiToken);

    [HttpGet("providers")]
    public IReadOnlyList<WireGuardProviderDto> Providers() => new[]
    {
        new WireGuardProviderDto(WireGuardProvider.WUi, "W-UI (WireGuard / AmneziaWG / OpenVPN)", true),
    };

    [HttpGet("panels")]
    public IReadOnlyList<WireGuardPanelDto> List() => _store.GetWireGuardPanels().Select(ToDto).ToList();

    // Verify a URL + credentials WITHOUT saving — the wizard's "test" button. Lets the owner confirm the
    // panel answers before committing anything to the store.
    [HttpPost("test")]
    public async Task<IActionResult> Test(WireGuardPanelInput input, CancellationToken ct)
    {
        var result = await _connector.TestAsync(input.Provider, new WireGuardCredentials(input.Url, input.Username, input.Password, input.ApiToken ?? ""), ct);
        return result.Ok
            ? Ok(new { ok = true, interfaceCount = result.InterfaceCount, version = result.Version })
            : Problem(result.Error);
    }

    // Add a panel. The connection is verified first: a panel that cannot be reached is a configuration
    // mistake, and storing it silently would only surface later as a failed order.
    [HttpPost("panels")]
    public async Task<IActionResult> Add(WireGuardPanelInput input, CancellationToken ct)
    {
        var url = IWireGuardPanelConnector.NormalizeUrl(input.Url);
        if (url is null) return Problem(UrlHint);
        var hasToken = !string.IsNullOrWhiteSpace(input.ApiToken);
        if (!hasToken && (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password)))
            return Problem("توکن دسترسی یا نام کاربری و گذرواژه پنل را وارد کنید.");

        var test = await _connector.TestAsync(input.Provider, new WireGuardCredentials(url, input.Username, input.Password, input.ApiToken ?? ""), ct);
        if (!test.Ok) return Problem(test.Error);

        var saved = _store.AddWireGuardPanel(new WireGuardPanel
        {
            Provider = input.Provider,
            Url = url,
            Username = (input.Username ?? "").Trim(),
            Password = input.Password ?? "",
            ApiToken = (input.ApiToken ?? "").Trim(),
            Name = (input.Name ?? "").Trim(),
            Remark = (input.Remark ?? "").Trim(),
            Flag = (input.Flag ?? "").Trim(),
            Capacity = Math.Max(0, input.Capacity),
            Enabled = true,
            LastCheckAtUtc = DateTime.UtcNow.ToString("O"),
            LastCheckOk = true,
            InterfaceCount = test.InterfaceCount,
            PanelVersion = test.Version,
        });
        return Ok(ToDto(saved));
    }

    // The tunnels of a stored panel — a live read, so it both proves the connection and gives the operator
    // the list a plan is mapped onto.
    [HttpGet("panels/{id:int}/interfaces")]
    public async Task<IActionResult> Interfaces(int id, CancellationToken ct)
    {
        var panel = _store.GetWireGuardPanel(id);
        if (panel is null) return NotFound();

        var result = await _connector.ListInterfacesAsync(panel.Provider, Creds(panel), ct);
        return result.Ok ? Ok(result.Interfaces) : Problem(result.Error);
    }

    // Re-test a stored panel and record the outcome so the list shows a fresh status.
    [HttpPost("panels/{id:int}/test")]
    public async Task<IActionResult> TestStored(int id, CancellationToken ct)
    {
        var panel = _store.GetWireGuardPanel(id);
        if (panel is null) return NotFound();

        var result = await _connector.TestAsync(panel.Provider, Creds(panel), ct);
        _store.RecordWireGuardPanelCheck(id, result.Ok, result.Error ?? "", result.InterfaceCount, result.Version);
        return result.Ok
            ? Ok(new { ok = true, interfaceCount = result.InterfaceCount, version = result.Version })
            : Problem(result.Error);
    }

    // Create a customer on a stored panel, on exactly the chosen tunnels. This is the call fulfilment will
    // make; exposed to the owner so the flow can be exercised before any purchase wiring.
    [HttpPost("panels/{id:int}/client")]
    public async Task<IActionResult> AddClient(int id, WireGuardNewClientInput input, CancellationToken ct)
    {
        var panel = _store.GetWireGuardPanel(id);
        if (panel is null) return NotFound();
        if (string.IsNullOrWhiteSpace(input.Name)) return Problem("نام مشتری را وارد کنید.");

        var result = await _connector.AddClientAsync(
            panel.Provider, Creds(panel),
            new WireGuardNewClient(input.Name.Trim(), input.TotalGb, Math.Max(1, input.DeviceLimit), input.DurationDays, input.StartOnFirstUse),
            input.InterfaceIds ?? Array.Empty<int>(),
            ct);

        return result.Ok
            ? Ok(new
            {
                ok = true,
                clientId = result.ClientId,
                subId = result.SubId,
                interfacesAdded = result.InterfacesAdded,
                // The link the customer's app is pointed at, as the panel itself builds it.
                subscriptionUrl = result.SubscriptionUrl,
            })
            : Problem(result.Error);
    }

    // Edit a stored panel. Same verify-before-save rule as Add. Password/apiToken are never sent back to the
    // browser, so a blank field here means "keep the stored value", not "clear it".
    [HttpPut("panels/{id:int}")]
    public async Task<IActionResult> Update(int id, WireGuardPanelInput input, CancellationToken ct)
    {
        var existing = _store.GetWireGuardPanel(id);
        if (existing is null) return NotFound();

        var url = IWireGuardPanelConnector.NormalizeUrl(input.Url);
        if (url is null) return Problem(UrlHint);

        var password = string.IsNullOrEmpty(input.Password) ? existing.Password : input.Password;
        var apiToken = string.IsNullOrWhiteSpace(input.ApiToken) ? existing.ApiToken : input.ApiToken;
        if (string.IsNullOrWhiteSpace(apiToken) && (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrEmpty(password)))
            return Problem("توکن دسترسی یا نام کاربری و گذرواژه پنل را وارد کنید.");

        var test = await _connector.TestAsync(input.Provider, new WireGuardCredentials(url, input.Username, password, apiToken ?? ""), ct);
        if (!test.Ok) return Problem(test.Error);

        var updated = _store.UpdateWireGuardPanel(id, new WireGuardPanel
        {
            Provider = input.Provider,
            Url = url,
            Username = (input.Username ?? "").Trim(),
            Password = password ?? "",
            ApiToken = apiToken ?? "",
            Name = (input.Name ?? "").Trim(),
            Remark = (input.Remark ?? "").Trim(),
            Flag = (input.Flag ?? "").Trim(),
            Capacity = Math.Max(0, input.Capacity),
            LastCheckAtUtc = DateTime.UtcNow.ToString("O"),
            LastCheckOk = true,
            InterfaceCount = test.InterfaceCount,
            PanelVersion = test.Version,
        });
        return updated is null ? NotFound() : Ok(ToDto(updated));
    }

    [HttpDelete("panels/{id:int}")]
    public IActionResult Delete(int id) =>
        _store.DeleteWireGuardPanel(id) ? Ok(new { ok = true }) : NotFound();
}
