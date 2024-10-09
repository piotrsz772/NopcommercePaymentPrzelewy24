using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nop.Web.Framework.Mvc.Routing;

namespace Nop.Plugin.Payments.Przelewy24.Infrastructure;

public class RouteProvider : IRouteProvider
{
    public void RegisterRoutes(IEndpointRouteBuilder endpointRouteBuilder)
    {
        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Payments.Przelewy24.Configure",
            pattern: "Plugins/PaymentPrzelewy24/Configure",
            defaults: new { controller = "PaymentPrzelewy24", action = "Configure" }
        );

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Payments.Przelewy24.Status",
            pattern: "PaymentPrzelewy24/Status",
            defaults: new { controller = "PaymentPrzelewy24", action = "Status" }
        );

        endpointRouteBuilder.MapControllerRoute(
            name: "Plugin.Payments.Przelewy24.Return",
            pattern: "PaymentPrzelewy24/Return/{id}",
            defaults: new { controller = "PaymentPrzelewy24", action = "Return" }
        );
    }

    public int Priority => 0;
}
