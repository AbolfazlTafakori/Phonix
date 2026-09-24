using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A renewal posts the client back as the panel served it, and some panel builds SERVE a field in one shape
// and BIND it in another: `allowedIPs` comes back as a string and is bound as []string, so a faithful echo
// is rejected with "cannot unmarshal string into Go struct field Client.allowedIPs of type []string" and
// the customer's paid renewal never lands.
//
// Driven against a real listener rather than a stub, because the behaviour under test is the connector's
// HTTP conversation with the panel — the retry, and what it sends the second time.
public class V2RayRenewalFieldShapeTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _url;
    private readonly List<string> _updates = new();
    private readonly Task _serving;

    // What the panel serves for allowedIPs. A string either way — that is the quirk — but a test can put
    // real entries in it to prove they survive the reshape.
    private string _servedAllowedIps = "";

    // How the panel answers an update. Default: the real quirk — a string where a list was wanted.
    private Func<string, (bool ok, string msg)> _onUpdate =
        body => JsonDocument.Parse(body).RootElement.TryGetProperty("allowedIPs", out var v) && v.ValueKind == JsonValueKind.String
            ? (false, "Something went wrong (json: cannot unmarshal string into Go struct field Client.allowedIPs of type []string)")
            : (true, "");

    public V2RayRenewalFieldShapeTests()
    {
        // Port 0 is not available to HttpListener, so a free high port is taken and retried on a clash.
        var port = new Random().Next(45000, 46000);
        for (var i = 0; i < 20; i++)
        {
            try
            {
                _listener.Prefixes.Clear();
                _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                _listener.Start();
                break;
            }
            catch (HttpListenerException) { port++; }
        }
        _url = $"http://127.0.0.1:{port}";
        _serving = Task.Run(ServeAsync);
    }

    public void Dispose()
    {
        try { _listener.Stop(); } catch { /* already down */ }
        try { _serving.Wait(TimeSpan.FromSeconds(2)); } catch { /* the listener throws on stop */ }
        ((IDisposable)_listener).Dispose();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }

            var path = ctx.Request.Url!.AbsolutePath;
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            var reply = path switch
            {
                // The client as the panel serves it — note allowedIPs as a STRING.
                var p when p.StartsWith("/panel/api/clients/get/") =>
                    """{"success":true,"obj":{"client":{"id":9,"uuid":"uuid-1","email":"px-1","enable":false,"totalGB":1,"expiryTime":1,"limitIp":1,"flow":"","subId":"sub-1","tgId":0,"comment":"","reset":0,"allowedIPs":"__ALLOWED__"}}}"""
                        .Replace("__ALLOWED__", _servedAllowedIps),
                var p when p.StartsWith("/panel/api/clients/update/") => Update(body),
                var p when p.StartsWith("/panel/api/clients/resetTraffic/") => """{"success":true}""",
                _ => """{"success":true,"obj":[]}""",
            };

            var bytes = Encoding.UTF8.GetBytes(reply);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    private string Update(string body)
    {
        _updates.Add(body);
        var (ok, msg) = _onUpdate(body);
        return JsonSerializer.Serialize(new { success = ok, msg });
    }

    private V2RayPanelConnector Connector() => new(NullLogger<V2RayPanelConnector>.Instance);
    private V2RayCredentials Creds() => new(_url, "", "", "token-1");

    [Fact]
    public async Task A_field_the_panel_serves_as_a_string_and_binds_as_a_list_is_reshaped_and_retried()
    {
        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(50, 30, 2));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(2, _updates.Count);   // the faithful echo, then the repaired one

        // The retry sends the field as the list the panel asked for, and still carries the term that was
        // bought — repairing one field must not drop the renewal itself.
        using var second = JsonDocument.Parse(_updates[1]);
        var root = second.RootElement;
        Assert.Equal(JsonValueKind.Array, root.GetProperty("allowedIPs").ValueKind);
        Assert.Equal(0, root.GetProperty("allowedIPs").GetArrayLength());
        Assert.Equal(50L * 1024 * 1024 * 1024, root.GetProperty("totalGB").GetInt64());
        Assert.Equal(2, root.GetProperty("limitIp").GetInt32());
        Assert.True(root.GetProperty("enable").GetBoolean());
        // The client's own identity is untouched: a renewal must not rotate what the customer's app holds.
        Assert.Equal("uuid-1", root.GetProperty("id").GetString());
        Assert.Equal("sub-1", root.GetProperty("subId").GetString());
    }

    [Fact]
    public async Task A_populated_string_keeps_its_entries_when_it_becomes_a_list()
    {
        // The operator had actually set an allowlist: reshaping it must not quietly empty it, or the renewal
        // would hand the customer a service open to addresses the operator had shut out.
        _servedAllowedIps = "1.2.3.4, 5.6.7.8";

        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(10, 30, 1));

        Assert.True(result.Ok, result.Error);
        using var doc = JsonDocument.Parse(_updates[^1]);
        var list = doc.RootElement.GetProperty("allowedIPs");
        Assert.Equal(JsonValueKind.Array, list.ValueKind);
        Assert.Equal(new[] { "1.2.3.4", "5.6.7.8" }, list.EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task A_failure_that_is_not_a_shape_problem_is_reported_as_it_came()
    {
        _onUpdate = _ => (false, "این اکانت روی پنل پیدا نشد.");

        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(10, 30, 1));

        Assert.False(result.Ok);
        Assert.Equal("این اکانت روی پنل پیدا نشد.", result.Error);
        Assert.Single(_updates);   // nothing to repair, so nothing retried
    }

    [Fact]
    public async Task The_same_field_is_never_repaired_twice()
    {
        // A panel that rejects the field whatever shape it is in must end the attempt, not spin on it.
        _onUpdate = _ => (false, "json: cannot unmarshal string into Go struct field Client.allowedIPs of type []string");

        var result = await Connector().RenewClientAsync(
            V2RayProvider.Sanaei, Creds(), "px-1", new V2RayClientLimits(10, 30, 1));

        Assert.False(result.Ok);
        Assert.Equal(2, _updates.Count);
    }
}
