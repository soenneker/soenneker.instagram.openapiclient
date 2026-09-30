using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Soenneker.Instagram.OpenApiClient.Models;
using Soenneker.Instagram.OpenApiClient.Item.Media_publish;

namespace Soenneker.Instagram.OpenApiClient.Tests;

public sealed class PublishingTests
{
    [Test]
    public async ValueTask CreatesAndPublishesMediaUsingReturnedContainerId()
    {
        int count = 0;
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            var form = await ReadForm(request);
            if (++count == 1)
            {
                Check(request.RequestUri!.AbsolutePath == "/v26.0/123/media", "Creation endpoint");
                Check(form["image_url"] == "https://example.com/image.jpg?a=1&b=2", "Image URL");
                Check(form["caption"] == "Hello & café", "Caption encoding");
                return Json("""{"id":"17999999999999999"}""");
            }
            Check(request.RequestUri!.AbsolutePath == "/v26.0/123/media_publish", "Publishing endpoint");
            Check(form["creation_id"] == "17999999999999999", "Container ID must be preserved as a string");
            return Json("""{"id":"18000000000000001"}""");
        }));
        var client = Create(http);
        var container = await client["123"].Media.PostAsync(new PostIdMediaXWwwFormUrlencodedRequest { ImageUrl = "https://example.com/image.jpg?a=1&b=2", Caption = "Hello & café" });
        var published = await client["123"].Media_publish.PostAsync(new PostIdMediaPublishXWwwFormUrlencodedRequest { CreationId = container!.Id });
        Check(published?.Id == "18000000000000001" && count == 2, "Published media ID");
    }

    [Test]
    public async ValueTask SendsCarouselChildrenAsJsonAndReadsContainerStatus()
    {
        using var http = new HttpClient(new Handler(async (request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                Check(request.RequestUri!.Query.Contains("fields="), "Requested container fields");
                return Json("""{"id":"container","status_code":"FINISHED","status":"Ready"}""");
            }
            var form = await ReadForm(request);
            Check(form["media_type"] == "CAROUSEL", "Carousel type");
            Check(form["children"] == """["child1","child2"]""", "Children JSON string");
            return Json("""{"id":"container"}""");
        }));
        var client = Create(http);
        await client["123"].Media.PostAsync(new PostIdMediaXWwwFormUrlencodedRequest { MediaType = "CAROUSEL", Children = """["child1","child2"]""" });
        var status = await client["container"].GetAsync(config => config.QueryParameters.Fields = "status_code,status");
        Check(status?.StatusCode == "FINISHED", "Container status must be typed");
    }

    [Test]
    public async ValueTask PropagatesApiErrorsAndCancellation()
    {
        using var http = new HttpClient(new Handler((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Json("""{"error":{"message":"Invalid token","type":"OAuthException","code":190}}""", HttpStatusCode.Unauthorized));
        }));
        var client = Create(http);
        bool failed = false;
        try { await client["123"].Media_publish.PostAsync(new PostIdMediaPublishXWwwFormUrlencodedRequest { CreationId = "container" }); }
        catch (ApiException error) { Check(error.ResponseStatusCode == 401, "HTTP error status"); failed = true; }
        Check(failed, "API error was swallowed");
        try { await client["123"].Media.GetAsync(cancellationToken: new CancellationToken(true)); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Cancellation was swallowed");
    }
    [Test]
    public async ValueTask ReadsInsightsOutsidePublishingSubset()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Check(request.RequestUri!.AbsolutePath == "/v26.0/123/insights", "Insights endpoint");
            return Task.FromResult(Json("""{"data":[{"name":"reach","period":"day","values":[{"value":42}]}]}"""));
        }));
        var result = await Create(http)["123"].Insights.GetAsync();
        Check(result is not null, "Insights response deserialized");
    }
    private static InstagramOpenApiClient Create(HttpClient http)
    {
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        return new InstagramOpenApiClient(new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: http));
    }

    private static async Task<Dictionary<string, string>> ReadForm(HttpRequestMessage request)
    {
        Check(request.Method == HttpMethod.Post, "POST request expected");
        Check(request.Headers.Authorization?.ToString() == "Bearer test-token", "Bearer token missing");
        Check(request.Content?.Headers.ContentType?.MediaType == "application/x-www-form-urlencoded", "Expected URL-encoded form");
        return (await request.Content!.ReadAsStringAsync()).Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).ToDictionary(x => WebUtility.UrlDecode(x[0]), x => WebUtility.UrlDecode(x.Length > 1 ? x[1] : ""));
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
