using Soenneker.Tests.HostedUnit;

namespace Soenneker.Instagram.OpenApiClient.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class InstagramOpenApiClientTests : HostedUnitTest
{
    public InstagramOpenApiClientTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
