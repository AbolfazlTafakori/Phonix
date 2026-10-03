using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Phonix.Api.Tests;

// The catalogue is ~500 KB of JSON and nginx only gzips HTML, so the API compresses its own reads. These pin
// that it does, that a client which can't decompress still gets plain JSON, and that auth stays uncompressed.
[Collection("api")]
public class ResponseCompressionTests : IClassFixture<PhonixAppFactory>
{
    private readonly HttpClient _client;

    public ResponseCompressionTests(PhonixAppFactory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("br")]
    [InlineData("gzip")]
    public async Task A_catalogue_read_is_compressed_and_still_decodes_to_the_same_json(string encoding)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.TryAddWithoutValidation("Accept-Encoding", encoding);

        var res = await _client.SendAsync(request);

        res.EnsureSuccessStatusCode();
        Assert.Equal(encoding, Assert.Single(res.Content.Headers.ContentEncoding));
        Assert.Contains("Accept-Encoding", res.Headers.Vary);
        await using var raw = await res.Content.ReadAsStreamAsync();
        await using Stream body = encoding == "br"
            ? new BrotliStream(raw, CompressionMode.Decompress)
            : new GZipStream(raw, CompressionMode.Decompress);
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.True(json.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public async Task A_client_that_asks_for_nothing_gets_plain_json()
    {
        var res = await _client.GetAsync("/api/products");

        res.EnsureSuccessStatusCode();
        Assert.Empty(res.Content.Headers.ContentEncoding);
        Assert.NotEmpty((await res.Content.ReadFromJsonAsync<JsonElement[]>())!);
    }

    [Fact]
    public async Task Auth_responses_are_never_compressed()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { identifier = "nobody-here", password = "wrong" }),
        };
        request.Headers.TryAddWithoutValidation("Accept-Encoding", "br, gzip");

        var res = await _client.SendAsync(request);

        Assert.Empty(res.Content.Headers.ContentEncoding);
    }
}
