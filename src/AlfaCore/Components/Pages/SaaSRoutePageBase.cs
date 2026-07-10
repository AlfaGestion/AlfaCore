using Microsoft.AspNetCore.Components;

namespace AlfaCore.Components.Pages;

public abstract class SaaSRoutePageBase : ComponentBase
{
    [Parameter]
    public string? idweb { get; set; }

    [Parameter]
    public int? idbase { get; set; }
}
