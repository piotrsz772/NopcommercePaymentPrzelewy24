using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Przelewy24.Models;

public class P24Notification
{
    public int p24_merchant_id { get; set; }
    public int p24_pos_id { get; set; }
    public string p24_session_id { get; set; }
    public int p24_amount { get; set; }
    public string p24_currency { get; set; }
    public int p24_order_id { get; set; }
    public int p24_method { get; set; }
    public string p24_statement { get; set; }
    public string p24_sign { get; set; }
}