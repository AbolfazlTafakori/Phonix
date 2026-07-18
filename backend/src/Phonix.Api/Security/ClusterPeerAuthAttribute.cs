using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Phonix.Api.Services;

namespace Phonix.Api.Security;

// Gates the node-to-node cluster endpoints (the peer pulling outbox entries, asking this node to demote,
// checking its liveness/role). The caller is another Phonix server, not a logged-in staff member, so this is
// deliberately independent of [Authorize]/the session cookie scheme — same shape as AdminPermissionAttribute
// (a small IAuthorizationFilter that inspects the request directly), but verifying an HMAC signature instead
// of a role claim. When clustering isn't configured on this node, every action behind this attribute 404s —
// not 401/403 — so a standalone install (the overwhelming majority) never reveals that the route even exists.
public sealed class ClusterPeerAuthAttribute : Attribute, IAsyncAuthorizationFilter
{
    private const long MaxSignedBodyBytes = 64 * 1024;


    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!ClusterAuth.IsConfigured)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var request = context.HttpContext.Request;

        // The signature covers the body, so the body has to be read before the caller is authenticated —
        // which means an unauthenticated request gets to allocate whatever it sends. Every node-to-node
        // payload here is a small JSON object (a cursor, a category/name pair, or {}), so anything larger is
        // refused outright rather than buffered to memory and disk on an internet-reachable endpoint.
        if (request.ContentLength is > MaxSignedBodyBytes)
        {
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
            return;
        }

        request.EnableBuffering(bufferThreshold: (int)MaxSignedBodyBytes, bufferLimit: MaxSignedBodyBytes);
        string body;
        try
        {
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            body = await reader.ReadToEndAsync();
        }
        catch (IOException)
        {
            // Thrown when a chunked request (no Content-Length) runs past the buffer limit.
            context.Result = new StatusCodeResult(StatusCodes.Status413PayloadTooLarge);
            return;
        }
        request.Body.Position = 0;

        var timestamp = request.Headers[ClusterAuth.TimestampHeader].ToString();
        var signature = request.Headers[ClusterAuth.SignatureHeader].ToString();
        var path = request.Path.Value ?? "";

        if (!ClusterAuth.Verify(request.Method, path, body, timestamp, signature))
        {
            context.Result = new UnauthorizedResult();
        }
    }
}
