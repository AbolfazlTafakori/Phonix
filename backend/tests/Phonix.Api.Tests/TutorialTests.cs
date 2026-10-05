using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// Product tutorials: written in the panel, linked to products, and shown — text and videos — only to people
// who have paid for one of those products.
public class TutorialTests
{
    private static LocalFileStorageService Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "phonix-tutorial-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PHONIX_UPLOADS_DIR", root);
        return new LocalFileStorageService();
    }

    // The smallest thing that opens as an MP4: a 12-byte header whose box type is "ftyp".
    private static IFormFile Mp4(string name = "setup-guide.mp4")
    {
        var bytes = new byte[64];
        Encoding.ASCII.GetBytes("ftypisom").CopyTo(bytes, 4);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary() };
    }

    private static IFormFile Fake(string name, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", name) { Headers = new HeaderDictionary() };
    }

    private static TutorialsController As(IDataStore store, IFileStorageService files, AppUser user) =>
        new(store, files)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                        new Claim(ClaimTypes.Role, user.Role.ToString()),
                        new Claim(ClaimTypes.Name, user.Username),
                    }, "test")),
                },
            },
        };

    private static AppUser Admin(IDataStore s) => s.GetUsers().First(u => u.Role == UserRole.Admin);
    // Seed: user 1 = ali (customer, wallet 180,000); product 1 = Netflix, product 2 = Spotify.
    private static AppUser Ali(IDataStore s) => s.GetUser(1)!;

    private static TutorialDto Create(TutorialsController c, string title, int[] products, params TutorialVideoInput[] videos) =>
        Assert.IsType<TutorialDto>(Assert.IsType<OkObjectResult>(
            c.Create(new TutorialInput(title, "## مرحله ۱\nوارد برنامه شوید.", products.ToList(), videos.ToList(), 0, true)).Result).Value);

    private static TutorialVideoDto UploadVideo(TutorialsController c, IFormFile file) =>
        Assert.IsType<TutorialVideoDto>(Assert.IsType<OkObjectResult>(c.UploadVideo(file, default).Result.Result).Value);

    private static List<TutorialDto> Mine(TutorialsController c) =>
        Assert.IsAssignableFrom<IEnumerable<TutorialDto>>(Assert.IsType<OkObjectResult>(c.Mine().Result).Value).ToList();

    // A paid order: placed, then its payment approved (PendingApproval → Preparing).
    private static Order Buy(IDataStore s, int productId)
    {
        var order = s.PlaceOrder(Ali(s), new[] { (productId, 1, (int?)null) }, "کارت", fromWallet: false).Order!;
        return s.SetOrderStatus(order.Id, OrderStatus.Preparing)!;
    }

    [Fact]
    public void A_buyer_sees_the_tutorials_of_what_they_bought_and_nothing_else()
    {
        var store = TestStore.Create();
        var files = Files();
        var admin = As(store, files, Admin(store));
        var netflix = Create(admin, "نصب نتفلیکس روی تلویزیون", new[] { 1 });
        Create(admin, "ورود به اسپاتیفای", new[] { 2 });
        var shared = Create(admin, "نکات امنیتی اکانت مشترک", new[] { 1, 2 });
        Buy(store, 1);

        var mine = Mine(As(store, files, Ali(store)));

        Assert.Equal(new[] { netflix.Id, shared.Id }.OrderBy(x => x), mine.Select(t => t.Id).OrderBy(x => x));
    }

    [Fact]
    public void Nothing_shows_before_the_payment_is_confirmed_or_after_a_cancel()
    {
        var store = TestStore.Create();
        var files = Files();
        Create(As(store, files, Admin(store)), "نصب نتفلیکس", new[] { 1 });
        var customer = As(store, files, Ali(store));

        var order = store.PlaceOrder(Ali(store), new[] { (1, 1, (int?)null) }, "کارت", fromWallet: false).Order!;
        Assert.Equal(OrderStatus.PendingApproval, order.Status);
        Assert.Empty(Mine(customer));

        store.SetOrderStatus(order.Id, OrderStatus.Preparing);
        Assert.Single(Mine(customer));

        store.SetOrderStatus(order.Id, OrderStatus.Cancelled);
        Assert.Empty(Mine(customer));
    }

    [Fact]
    public void A_switched_off_tutorial_is_hidden_from_buyers()
    {
        var store = TestStore.Create();
        var files = Files();
        var admin = As(store, files, Admin(store));
        var t = Create(admin, "نصب نتفلیکس", new[] { 1 });
        Buy(store, 1);

        admin.Update(t.Id, new TutorialInput(t.Title, t.Body, t.ProductIds, new(), 0, IsActive: false));

        Assert.Empty(Mine(As(store, files, Ali(store))));
    }

    [Fact]
    public void Only_a_buyer_and_staff_can_play_a_tutorial_video()
    {
        var store = TestStore.Create();
        var files = Files();
        var admin = As(store, files, Admin(store));
        var video = UploadVideo(admin, Mp4());
        Create(admin, "نصب نتفلیکس", new[] { 1 }, new TutorialVideoInput(video.Id, video.Name));
        var customer = As(store, files, Ali(store));

        Assert.IsType<NotFoundResult>(customer.Video(video.Id));      // hasn't bought it
        Buy(store, 1);
        var played = Assert.IsType<FileStreamResult>(customer.Video(video.Id));
        Assert.True(played.EnableRangeProcessing);                    // the player can seek
        Assert.Equal("video/mp4", played.ContentType);
        played.FileStream.Dispose();
        var staffView = Assert.IsType<FileStreamResult>(admin.Video(video.Id));
        staffView.FileStream.Dispose();
    }

    [Fact]
    public void A_file_that_only_calls_itself_a_video_is_refused()
    {
        var store = TestStore.Create();
        var admin = As(store, Files(), Admin(store));

        Assert.IsType<BadRequestObjectResult>(admin.UploadVideo(Fake("trojan.mp4", "<script>alert(1)</script> padding padding"), default).Result.Result);
        Assert.IsType<BadRequestObjectResult>(admin.UploadVideo(Fake("clip.mov", "anything"), default).Result.Result);
    }

    [Fact]
    public void Removing_a_video_from_a_tutorial_deletes_its_file_and_so_does_deleting_the_tutorial()
    {
        var store = TestStore.Create();
        var files = Files();
        var admin = As(store, files, Admin(store));
        var first = UploadVideo(admin, Mp4("part-1.mp4"));
        var second = UploadVideo(admin, Mp4("part-2.mp4"));
        var t = Create(admin, "نصب", new[] { 1 }, new(first.Id, first.Name), new(second.Id, second.Name));

        admin.Update(t.Id, new TutorialInput(t.Title, t.Body, t.ProductIds, new() { new(second.Id, second.Name) }, 0, true));
        Assert.Null(files.OpenVideo(first.Id));
        var kept = files.OpenVideo(second.Id);
        Assert.NotNull(kept);
        kept!.Content.Dispose();

        Assert.IsType<NoContentResult>(admin.Delete(t.Id));
        Assert.Null(files.OpenVideo(second.Id));
        Assert.Empty(store.GetTutorials());
    }

    [Fact]
    public void A_video_id_that_was_never_uploaded_is_not_attached()
    {
        var store = TestStore.Create();
        var admin = As(store, Files(), Admin(store));

        var t = Create(admin, "نصب", new[] { 1 }, new TutorialVideoInput("1__0123456789abcdef0123456789abcdef.mp4", "ghost"));

        Assert.Empty(t.Videos);
    }

    [Fact]
    public void A_tutorial_needs_a_title()
    {
        var store = TestStore.Create();
        var admin = As(store, Files(), Admin(store));

        Assert.IsType<BadRequestObjectResult>(admin.Create(new TutorialInput("  ", "متن", new() { 1 }, null, 0, true)).Result);
    }

    [Fact]
    public void Tutorials_travel_with_a_backup()
    {
        var store = TestStore.Create();
        Create(As(store, Files(), Admin(store)), "نصب نتفلیکس روی تلویزیون", new[] { 1 });

        // The snapshot escapes non-ASCII text, so read it back rather than searching the raw JSON.
        using var json = System.Text.Json.JsonDocument.Parse(store.SerializeSnapshot());
        var items = json.RootElement.EnumerateObject().First(p => p.NameEquals("Tutorials") || p.NameEquals("tutorials"))
            .Value.EnumerateObject().First(p => p.Name is "Items" or "items").Value;
        Assert.Equal("نصب نتفلیکس روی تلویزیون",
            items[0].EnumerateObject().First(p => p.Name is "Title" or "title").Value.GetString());
    }
}
