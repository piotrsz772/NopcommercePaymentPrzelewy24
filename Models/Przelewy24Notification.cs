using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Nop.Plugin.Payments.Przelewy24.Models;
public class Przelewy24Notification
{
    [JsonProperty("p24_merchantId")]
    public int P24MerchantId { get; set; }

    [JsonProperty("p24_posId")]
    public int P24PosId { get; set; }

    [JsonProperty("p24_session_id")]
    public string P24SessionId { get; set; }

    [JsonProperty("p24_amount")]
    public int P24Amount { get; set; }

    [JsonProperty("p24_currency")]
    public string P24Currency { get; set; }

    [JsonProperty("p24_orderId")]
    public int P24OrderId { get; set; }

    [JsonProperty("p24_sign")]
    public string P24Sign { get; set; }

    [JsonProperty("p24_statement")]
    public string P24Statement { get; set; }

    [JsonProperty("p24_method")]
    public string P24Method { get; set; }
}
