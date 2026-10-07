using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Phonix.Api.Controllers;
using Phonix.Api.Models;
using Phonix.Api.Services;
using Xunit;

namespace Phonix.Api.Tests;

// A ticket's attachment is shown to staff as a link and forwarded to the support group. Only a picture uploaded
// to this site is kept: a link to anywhere else, sent straight to the API, is dropped instead of being presented
// to staff as something the customer attached.
public class TicketAttachmentTests
{
    private sealed class NoMail : IEmailSender
    {
        public Task<bool> SendAsync(string to, string subject, string body, string? htmlBody = null) => Task.FromResult(true);
    }

    private static TicketsController AsCustomer(Phonix.Api.Data.IDataStore store)
    {
        var controller = new TicketsController(store, new UserMailer(store, new NoMail(), NullLogger<UserMailer>.Instance));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "Customer"), new Claim(ClaimTypes.Name, "ali"),
                }, "test")),
            },
        };
        return controller;
    }

    [Theory]
    [InlineData("https://evil.example/login")]
    [InlineData("javascript:alert(1)")]
    [InlineData("//evil.example/x.png")]
    [InlineData("/api/upload/../../etc/passwd")]
    public void Anything_but_a_site_upload_is_dropped(string attachment)
    {
        var store = TestStore.Create();

        var ticket = AsCustomer(store).Create(new CreateTicketInput("موضوع", "فنی", "متن", TicketPriority.Medium, attachment)).Value!;

        Assert.Equal("", ticket.Attachment);
    }

    [Fact]
    public void A_site_upload_is_kept_on_the_ticket_and_on_a_reply()
    {
        var store = TestStore.Create();
        const string upload = "/api/upload/1__0123456789abcdef0123456789abcdef.png";
        var controller = AsCustomer(store);

        var ticket = controller.Create(new CreateTicketInput("موضوع", "فنی", "متن", TicketPriority.Medium, upload)).Value!;
        var replied = controller.Reply(ticket.Id, new TicketReplyInput("ادامه", false, "https://evil.example/x.png")).Value!;

        Assert.Equal(upload, ticket.Attachment);
        Assert.Equal("", replied.Messages.Last().Attachment);
    }
}
