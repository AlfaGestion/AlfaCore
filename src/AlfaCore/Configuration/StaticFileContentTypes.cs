using Microsoft.AspNetCore.StaticFiles;

namespace AlfaCore.Configuration;

public static class StaticFileContentTypes
{
    public static FileExtensionContentTypeProvider CreateProvider()
    {
        var provider = new FileExtensionContentTypeProvider();
        provider.Mappings[".webmanifest"] = "application/manifest+json";
        return provider;
    }
}
