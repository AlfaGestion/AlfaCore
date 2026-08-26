using AlfaCore.Configuration;
using Xunit;

namespace AlfaCore.Tests;

public sealed class StaticFileContentTypesTests
{
    [Fact]
    public void WebManifestUsesManifestJsonContentType()
    {
        var provider = StaticFileContentTypes.CreateProvider();

        Assert.True(provider.TryGetContentType("manifest.webmanifest", out var contentType));
        Assert.Equal("application/manifest+json", contentType);
    }
}
