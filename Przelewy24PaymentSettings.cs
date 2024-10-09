using Nop.Core.Configuration;
using System;

namespace Nop.Plugin.Payments.Przelewy24
{
    public class Przelewy24PaymentSettings : ISettings
    {
        public string MerchantId { get; set; }
        public string CRC { get; set; }
        public bool UseSandbox { get; set; }
        public string Description { get; set; }
        public string TransferLabel { get; set; }
        public int PaymentTimeLimit { get; set; }
        public decimal AdditionalFee { get; set; }
        public bool AdditionalFeePercentage { get; set; }
    }
}
