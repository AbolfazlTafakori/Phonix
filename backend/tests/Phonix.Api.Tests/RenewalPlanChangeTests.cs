using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Renewing is not only "the same thing again": a customer on a 10 GB one-device plan may renew onto 80 GB
// with three devices, or drop back down. What they picked has to reach the panel — the traffic, the term
// AND the device/IP allowance — on both panel kinds, in both directions.
//
// A real listener stands in for the panel, because what is under test is the conversation: which requests
// go out, and what they carry.
public abstract class PanelListenerTests : IDisposable
{
    protected readonly HttpListener Listener = new();
    protected readonly string Url;
    protected readonly List<(string method, string path, string body)> Calls = new();
    private readonly Task _serving;

    protected PanelListenerTests()
    {
        var port = new Random().Next(46000, 47000);
        for (var i = 0; i < 20; i++)
        {
            try
            {
                Listener.Prefixes.Clear();
                Listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                Listener.Start();
                break;
            }
            catch (HttpListenerException) { port++; }
        }
        Url = $"http://127.0.0.1:{port}";
        _serving = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        try { Listener.Stop(); } catch { /* already down */ }
        try { _serving.Wait(TimeSpan.FromSeconds(2)); } catch { /* the listener throws on stop */ }
        ((IDisposable)Listener).Dispose();
        GC.SuppressFinalize(this);
    }

    protected abstract string Reply(string method, string path, string body);

    private async Task ServeAsync()
    {
        while (Listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await Listener.GetContextAsync(); }
            catch { return; }

            var path = ctx.Request.Url!.AbsolutePath;
            var method = ctx.Request.HttpMethod;
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            Calls.Add((method, path, body));
            var bytes = Encoding.UTF8.GetBytes(Reply(method, path, body));
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    protected JsonElement BodyOf(string method, string pathFragment)
    {
        var call = Calls.Last(c => c.method == method && c.path.Contains(pathFragment));
        return JsonDocument.Parse(call.body).RootElement.Clone();
    }
}

// ── 3x-ui ────────────────────────────────────────────────────────────────────────────────────────
public class V2RayRenewalPlanChangeTests : PanelListenerTests
{
    // The client as it stands before the renewal: a small plan, one IP.
    protected override string Reply(string method, string path, string body) =>
        path.StartsWith("/panel/api/clients/get/")
            ? """{"success":true,"obj":{"client":{"id":9,"uuid":"uuid-1","email":"px-1","enable":true,"totalGB":10737418240,"expiryTime":0,"limitIp":1,"flow":"","subId":"sub-1","tgId":0,"comment":"","reset":0}}}"""
            : """{"success":true}""";

    private V2RayPanelConnector Connector() => new(NullLogger<V2RayPanelConnector>.Instance);
    private V2RayCredentials Creds() => new(Url, "", "", "token-1");

    [Fact]
    public async Task Renewing_onto_a_bigger_plan_writes_the_bigger_terms()
    {
        // 10 GB / 1 IP  →  80 GB / 3 IPs
        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(80, 30, 3));

        Assert.True(result.Ok, result.Error);
        var sent = BodyOf("POST", "/panel/api/clients/update/");
        Assert.Equal(80L * 1024 * 1024 * 1024, sent.GetProperty("totalGB").GetInt64());
        Assert.Equal(3, sent.GetProperty("limitIp").GetInt32());
        Assert.True(sent.GetProperty("enable").GetBoolean());
        // Identity is never rewritten by a change of plan: the customer's app keeps working untouched.
        Assert.Equal("uuid-1", sent.GetProperty("id").GetString());
        Assert.Equal("sub-1", sent.GetProperty("subId").GetString());
    }

    [Fact]
    public async Task Renewing_onto_a_smaller_plan_writes_the_smaller_terms()
    {
        // 10 GB / 1 IP  →  5 GB / 1 IP. The quota must come DOWN, not be left at the old figure.
        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(5, 30, 1));

        Assert.True(result.Ok, result.Error);
        var sent = BodyOf("POST", "/panel/api/clients/update/");
        Assert.Equal(5L * 1024 * 1024 * 1024, sent.GetProperty("totalGB").GetInt64());
        Assert.Equal(1, sent.GetProperty("limitIp").GetInt32());
    }

    [Fact]
    public async Task Renewing_onto_an_unlimited_plan_clears_the_quota_and_the_expiry()
    {
        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(0, 0, 0));

        Assert.True(result.Ok, result.Error);
        var sent = BodyOf("POST", "/panel/api/clients/update/");
        Assert.Equal(0, sent.GetProperty("totalGB").GetInt64());     // 0 = unlimited, the panel's own convention
        Assert.Equal(0, sent.GetProperty("expiryTime").GetInt64());  // 0 = never expires
        Assert.Equal(0, sent.GetProperty("limitIp").GetInt32());
    }

    [Fact]
    public async Task The_old_terms_usage_is_cleared_so_a_bigger_plan_is_usable_at_once()
    {
        // Without this a customer who renewed after running out would still be exhausted on the new quota.
        await Connector().RenewClientAsync(V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(80, 30, 3));
        Assert.Contains(Calls, c => c.path.Contains("/panel/api/clients/resetTraffic/"));
    }
}

// ── W-UI ─────────────────────────────────────────────────────────────────────────────────────────
public class WireGuardRenewalPlanChangeTests : PanelListenerTests
{
    // The devices the customer holds before the renewal, and the limit the panel currently enforces. Account
    // id 10+n belongs to device-n, so a deletion can be checked by the id it was sent.
    private int _devices = 1;
    private int _limit = 1;

    protected override string Reply(string method, string path, string body)
    {
        if (path.StartsWith("/api/clients/") && path.EndsWith("/devices") && method == "POST")
        {
            _devices++;   // the panel issued another file
            return """{"id":99,"deviceName":"device-x"}""";
        }
        if (method == "GET" && path.StartsWith("/api/clients/"))
        {
            var accounts = string.Join(",", Enumerable.Range(1, _devices)
                .Select(i => $$"""{"id":{{10 + i}},"deviceName":"device-{{i}}","interfaceId":1}"""));
            return $$"""{"id":7,"name":"px-1","quotaBytes":10737418240,"usedBytes":0,"expiresAt":null,"status":"active","deviceLimit":{{_limit}},"onlineNow":0,"accounts":[{{accounts}}]}""";
        }
        return """{"ok":true}""";
    }

    private WireGuardPanelConnector Connector() => new(NullLogger<WireGuardPanelConnector>.Instance);
    private WireGuardCredentials Creds() => new(Url, "", "", "wui_token");

    [Fact]
    public async Task Renewing_onto_a_bigger_plan_issues_the_devices_it_pays_for()
    {
        // 10 GB / 1 device  →  80 GB / 3 devices. The limit alone is not enough: on W-UI a device IS a
        // configuration file, so two more have to be issued or the customer holds one file for a three-device
        // plan.
        var result = await Connector().RenewClientAsync(
            WireGuardProvider.WUi, Creds(), 7, new WireGuardClientLimits(80, 30, 3));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, Calls.Count(c => c.method == "POST" && c.path.EndsWith("/devices")));

        var sent = BodyOf("PATCH", "/api/clients/7");
        Assert.Equal(80L * 1024 * 1024 * 1024, sent.GetProperty("quotaBytes").GetInt64());
        Assert.Equal(3, sent.GetProperty("deviceLimit").GetInt32());
        Assert.Equal("active", sent.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Renewing_onto_a_smaller_plan_hands_the_extra_files_back()
    {
        // Three files issued, renewed onto a one-device plan. The panel refuses a limit below the number
        // already issued, so the extras have to go — and the EARLIEST is the one kept, because the first file
        // a customer imported is the one they are most likely still using.
        _devices = 3;
        _limit = 3;

        var result = await Connector().RenewClientAsync(
            WireGuardProvider.WUi, Creds(), 7, new WireGuardClientLimits(5, 30, 1));

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain(Calls, c => c.method == "POST" && c.path.EndsWith("/devices"));

        var deleted = Calls.Where(c => c.method == "DELETE").Select(c => c.path).ToList();
        Assert.Equal(2, deleted.Count);
        Assert.Contains("/api/devices/12", deleted);   // device-2
        Assert.Contains("/api/devices/13", deleted);   // device-3
        Assert.DoesNotContain("/api/devices/11", deleted);   // device-1 stays

        var sent = BodyOf("PATCH", "/api/clients/7");
        Assert.Equal(5L * 1024 * 1024 * 1024, sent.GetProperty("quotaBytes").GetInt64());
        Assert.Equal(1, sent.GetProperty("deviceLimit").GetInt32());
    }

    [Fact]
    public async Task The_allowance_is_raised_before_the_panel_is_asked_for_a_new_file()
    {
        // The panel refuses a file past the limit the client currently holds, so the order is not a detail:
        // limit up, then files, then the term.
        await Connector().RenewClientAsync(WireGuardProvider.WUi, Creds(), 7, new WireGuardClientLimits(80, 30, 3));

        var firstPatch = Calls.FindIndex(c => c.method == "PATCH");
        var firstDevice = Calls.FindIndex(c => c.method == "POST" && c.path.EndsWith("/devices"));
        Assert.True(firstPatch >= 0 && firstDevice > firstPatch);

        // …and that first PATCH only lifts the allowance: the term must not ride along with it, or a failure
        // while issuing files would leave the customer's expiry already extended.
        var lift = JsonDocument.Parse(Calls[firstPatch].body).RootElement;
        Assert.Equal(3, lift.GetProperty("deviceLimit").GetInt32());
        Assert.False(lift.TryGetProperty("expiresAt", out _));
        Assert.False(lift.TryGetProperty("quotaBytes", out _));
    }

    [Fact]
    public async Task Renewing_onto_the_same_size_issues_nothing_new()
    {
        var result = await Connector().RenewClientAsync(
            WireGuardProvider.WUi, Creds(), 7, new WireGuardClientLimits(10, 30, 1));

        Assert.True(result.Ok, result.Error);
        Assert.DoesNotContain(Calls, c => c.method == "POST" && c.path.EndsWith("/devices"));
    }

    [Fact]
    public async Task Renewing_onto_an_unlimited_plan_clears_the_quota_and_the_expiry()
    {
        var result = await Connector().RenewClientAsync(
            WireGuardProvider.WUi, Creds(), 7, new WireGuardClientLimits(0, 0, 1));

        Assert.True(result.Ok, result.Error);
        var sent = BodyOf("PATCH", "/api/clients/7");
        Assert.Equal(0, sent.GetProperty("quotaBytes").GetInt64());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("expiresAt").ValueKind);   // null = never expires
        Assert.Null(result.ExpiresAt);
    }

    [Fact]
    public async Task The_term_is_written_last()
    {
        // A failure while issuing files must leave the expiry untouched, so that retrying the renewal cannot
        // extend the customer's term a second time. The term is therefore the LAST thing written.
        await Connector().RenewClientAsync(WireGuardProvider.WUi, Creds(), 7, new WireGuardClientLimits(80, 30, 3));

        var lastDevice = Calls.FindLastIndex(c => c.method == "POST" && c.path.EndsWith("/devices"));
        var term = Calls.FindLastIndex(c => c.method == "PATCH");
        Assert.True(lastDevice >= 0 && term > lastDevice);
        Assert.True(JsonDocument.Parse(Calls[term].body).RootElement.TryGetProperty("expiresAt", out _));
    }
}
