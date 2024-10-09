using Nop.Web.Framework.Models;
using Nop.Web.Framework.Mvc.ModelBinding;
using System;

namespace Nop.Plugin.Payments.Przelewy24.Models;

public record ConfigurationModel : BaseNopModel
{
    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.MerchantId")]
    public string MerchantId { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.CRC")]
    public string CRC { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.UseSandbox")]
    public bool UseSandbox { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.Description")]
    public string Description { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.TransferLabel")]
    public string TransferLabel { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.PaymentTimeLimit")]
    public int PaymentTimeLimit { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.AdditionalFee")]
    public decimal AdditionalFee { get; set; }

    [NopResourceDisplayName("Plugins.Payments.Przelewy24.Fields.AdditionalFeePercentage")]
    public bool AdditionalFeePercentage { get; set; }
}
