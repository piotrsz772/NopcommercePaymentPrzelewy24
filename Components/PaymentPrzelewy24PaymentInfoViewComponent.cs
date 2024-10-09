using Microsoft.AspNetCore.Mvc;
using Nop.Plugin.Payments.Przelewy24.Models;
using Nop.Web.Framework.Components;

namespace Nop.Plugin.Payments.Przelewy24.Components
{
    [ViewComponent(Name = "PaymentPrzelewy24PaymentInfo")]
    public class PaymentPrzelewy24PaymentInfoViewComponent : NopViewComponent
    {
        private readonly Przelewy24PaymentSettings _przelewy24PaymentSettings;

        public PaymentPrzelewy24PaymentInfoViewComponent(Przelewy24PaymentSettings przelewy24PaymentSettings)
        {
            _przelewy24PaymentSettings = przelewy24PaymentSettings;
        }

        public IViewComponentResult Invoke()
        {
            var model = new PaymentInfoModel
            {
                DescriptionText = _przelewy24PaymentSettings.Description
            };

            return View("~/Plugins/Payments.Przelewy24/Views/PaymentInfo.cshtml", model);
        }
    }
}
