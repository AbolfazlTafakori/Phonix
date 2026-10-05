using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Phonix.Api.Controllers;
using Phonix.Api.Data;
using Phonix.Api.Models;
using Phonix.Api.Services;
using SkiaSharp;
using Xunit;

namespace Phonix.Api.Tests;

// The panel's image library: upload once, use the link anywhere, never delete a picture the site still shows,
// and never let the orphan sweep take an image that simply hasn't been used yet.
public class MediaLibraryTests
{
    private static LocalFileStorageService Files()
    {
        var root = Path.Combine(Path.GetTempPath(), "phonix-media-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("PHONIX_UPLOADS_DIR", root);
        return new LocalFileStorageService();
    }

    private static IFormFile Png(string fileName)
    {
        using var bitmap = new SKBitmap(12, 8);
        bitmap.Erase(SKColors.OrangeRed);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = data.ToArray();
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(), ContentType = "image/png",
        };
    }

    private static AppUser Admin(IDataStore store) => store.GetUsers().First(u => u.Role == UserRole.Admin);

    private static MediaController Controller(IDataStore store, IFileStorageService files)
    {
        var admin = Admin(store);
        return new MediaController(store, files)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, admin.Id.ToString()),
                        new Claim(ClaimTypes.Role, nameof(UserRole.Admin)),
                        new Claim(ClaimTypes.Name, admin.Username),
                    }, "test")),
                },
            },
        };
    }

    private static async Task<MediaItemDto> Upload(MediaController c, string name = "banner-autumn.png") =>
        Assert.IsType<MediaItemDto>(Assert.IsType<OkObjectResult>((await c.Upload(Png(name), default)).Result).Value);

    [Fact]
    public async Task An_upload_lands_in_the_library_with_its_name_and_a_link()
    {
        var store = TestStore.Create();
        var c = Controller(store, Files());

        var item = await Upload(c);

        Assert.Equal("banner-autumn", item.Name);
        Assert.Equal($"/api/upload/{item.Id}", item.Url);
        Assert.True(item.Size > 0);
        var listed = Assert.Single(c.List(), m => m.Id == item.Id);
        Assert.True(listed.InLibrary);
        Assert.False(listed.InUse);
        Assert.Equal(Admin(store).Username, listed.UploadedBy);
    }

    [Fact]
    public async Task A_library_image_nothing_uses_yet_survives_the_orphan_sweep()
    {
        var store = TestStore.Create();
        var files = Files();
        var item = await Upload(Controller(store, files));

        files.SweepPublicOrphans(store.SerializeSnapshot(), TimeSpan.Zero);

        var stored = files.Open("avatars", item.Id);
        Assert.NotNull(stored);
        stored!.Content.Dispose();
    }

    [Fact]
    public async Task An_unused_image_is_deleted_for_good()
    {
        var store = TestStore.Create();
        var files = Files();
        var c = Controller(store, files);
        var item = await Upload(c);

        Assert.IsType<NoContentResult>(c.Delete(item.Id));

        Assert.Null(files.Open("avatars", item.Id));
        Assert.DoesNotContain(store.GetMediaLibrary(), i => i.Id == item.Id);
        Assert.DoesNotContain(c.List(), m => m.Id == item.Id);
    }

    [Fact]
    public async Task An_image_the_site_still_shows_is_refused_and_kept()
    {
        var store = TestStore.Create();
        var files = Files();
        var c = Controller(store, files);
        var item = await Upload(c);
        var content = store.GetSiteContent();
        content.Brand.Logo = item.Url;
        store.UpdateSiteContent(content);

        Assert.True(c.List().Single(m => m.Id == item.Id).InUse);
        Assert.IsType<ConflictObjectResult>(c.Delete(item.Id));

        var stored = files.Open("avatars", item.Id);
        Assert.NotNull(stored);
        stored!.Content.Dispose();
        Assert.Contains(store.GetMediaLibrary(), i => i.Id == item.Id);
    }

    [Fact]
    public async Task Images_staff_uploaded_elsewhere_show_up_too()
    {
        var store = TestStore.Create();
        var files = Files();
        // A product photo uploaded through the ordinary upload before the library existed.
        var saved = await files.SavePublicImageAsync(Admin(store).Id, Png("product.png"));

        var listed = Assert.Single(Controller(store, files).List(), m => m.Id == saved.Id);
        Assert.False(listed.InLibrary);
    }

    [Fact]
    public async Task Customers_profile_pictures_are_not_site_imagery()
    {
        var store = TestStore.Create();
        var files = Files();
        var customer = store.GetUsers().First(u => u.Role == UserRole.Customer);
        var avatar = await files.SavePublicImageAsync(customer.Id, Png("me.png"));
        var c = Controller(store, files);

        Assert.DoesNotContain(c.List(), m => m.Id == avatar.Id);
        Assert.IsType<NotFoundResult>(c.Delete(avatar.Id!));
        var stored = files.Open("avatars", avatar.Id!);
        Assert.NotNull(stored);
        stored!.Content.Dispose();
    }

    [Fact]
    public async Task The_library_travels_with_a_backup()
    {
        var store = TestStore.Create();
        var item = await Upload(Controller(store, Files()));

        Assert.Contains(item.Id, store.SerializeSnapshot());
    }
}
