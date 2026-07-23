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
    string CreatedAtUtc, string LastCheckAtUtc, bool LastCheckOk, string LastCheckError, int InboundCount);

public sealed record V2RayPanelInput(V2RayProvider Provider, string Url, string Username, string Password);

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
        p.CreatedAtUtc, p.LastCheckAtUtc, p.LastCheckOk, p.LastCheckError, p.InboundCount);

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
        var result = await _connector.TestAsync(input.Provider, input.Url, input.Username, input.Password, ct);
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
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrWhiteSpace(input.Password))
            return Problem("نام کاربری و گذرواژه پنل را وارد کنید.");

        var test = await _connector.TestAsync(input.Provider, url, input.Username, input.Password, ct);
        if (!test.Ok) return Problem(test.Error);

        var saved = _store.AddV2RayPanel(new V2RayPanel
        {
            Provider = input.Provider,
            Url = url,
            Username = input.Username.Trim(),
            Password = input.Password,
            Enabled = true,
            LastCheckAtUtc = DateTime.UtcNow.ToString("O"),
            LastCheckOk = true,
            InboundCount = test.InboundCount,
        });
        return Ok(ToDto(saved));
    }

    // Re-test a stored panel and record the outcome so the list shows a fresh status.
    [HttpPost("panels/{id:int}/test")]
    public async Task<IActionResult> TestStored(int id, CancellationToken ct)
    {
        var panel = _store.GetV2RayPanel(id);
        if (panel is null) return NotFound();

        var result = await _connector.TestAsync(panel.Provider, panel.Url, panel.Username, panel.Password, ct);
        _store.RecordV2RayPanelCheck(id, result.Ok, result.Error ?? "", result.InboundCount);
        return result.Ok
            ? Ok(new { ok = true, inboundCount = result.InboundCount })
            : Problem(result.Error);
    }

    [HttpDelete("panels/{id:int}")]
    public IActionResult Delete(int id) =>
        _store.DeleteV2RayPanel(id) ? Ok(new { ok = true }) : NotFound();
}
