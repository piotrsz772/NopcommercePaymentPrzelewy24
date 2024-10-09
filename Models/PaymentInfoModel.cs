using Nop.Web.Framework.Models;

namespace Nop.Plugin.Payments.Przelewy24.Models
{
  public record PaymentInfoModel : BaseNopModel
  {
    public string DescriptionText { get; set; }
  }
}