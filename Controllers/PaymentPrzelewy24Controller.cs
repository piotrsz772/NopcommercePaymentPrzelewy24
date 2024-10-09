using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Nop.Core;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Plugin.Payments.Przelewy24.Models;
using Nop.Services.Configuration;
using Nop.Services.Customers;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Security;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Przelewy24.Controllers
{
    public class PaymentPrzelewy24Controller : BasePaymentController
    {
        private readonly ISettingService _settingService;
        private readonly IPaymentService _paymentService;
        private readonly IOrderService _orderService;
        private readonly IOrderProcessingService _orderProcessingService;
        private readonly IWebHelper _webHelper;
        private readonly Przelewy24PaymentSettings _przelewy24PaymentSettings;
        private readonly PaymentSettings _paymentSettings;
        private readonly ILogger _logger;
        private readonly INotificationService _notificationService;
        private readonly IPermissionService _permissionService;
        private readonly IStoreContext _storeContext;
        private readonly ICustomerService _customerService;

        public PaymentPrzelewy24Controller(
            ISettingService settingService,
            IPaymentService paymentService,
            IOrderService orderService,
            IOrderProcessingService orderProcessingService,
            IWebHelper webHelper,
            Przelewy24PaymentSettings przelewy24PaymentSettings,
            PaymentSettings paymentSettings,
            ILogger logger,
            INotificationService notificationService,
            IPermissionService permissionService,
            IStoreContext storeContext,
            ICustomerService customerService)
        {
            _settingService = settingService;
            _paymentService = paymentService;
            _orderService = orderService;
            _orderProcessingService = orderProcessingService;
            _webHelper = webHelper;
            _przelewy24PaymentSettings = przelewy24PaymentSettings;
            _paymentSettings = paymentSettings;
            _logger = logger;
            _notificationService = notificationService;
            _permissionService = permissionService;
            _storeContext = storeContext;
            _customerService = customerService;
        }

        [AuthorizeAdmin(false)]
        [Area("Admin")]
        public async Task<IActionResult> Configure()
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var przelewy24PaymentSettings = await _settingService.LoadSettingAsync<Przelewy24PaymentSettings>(storeScope);

            var model = new ConfigurationModel
            {
                MerchantId = przelewy24PaymentSettings.MerchantId,
                CRC = przelewy24PaymentSettings.CRC,
                UseSandbox = przelewy24PaymentSettings.UseSandbox,
                Description = przelewy24PaymentSettings.Description,
                TransferLabel = przelewy24PaymentSettings.TransferLabel,
                PaymentTimeLimit = przelewy24PaymentSettings.PaymentTimeLimit,
                AdditionalFee = przelewy24PaymentSettings.AdditionalFee,
                AdditionalFeePercentage = przelewy24PaymentSettings.AdditionalFeePercentage
            };
            return View("~/Plugins/Payments.Przelewy24/Views/Configure.cshtml", model);
        }

        [HttpPost]
        [AuthorizeAdmin(false)]
        [Area("Admin")]
        public async Task<IActionResult> Configure(ConfigurationModel model)
        {
            if (!await _permissionService.AuthorizeAsync(StandardPermissionProvider.ManagePaymentMethods))
                return AccessDeniedView();

            if (!ModelState.IsValid)
                return await Configure();

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var przelewy24PaymentSettings = await _settingService.LoadSettingAsync<Przelewy24PaymentSettings>(storeScope);

            przelewy24PaymentSettings.MerchantId = model.MerchantId;
            przelewy24PaymentSettings.CRC = model.CRC;
            przelewy24PaymentSettings.UseSandbox = model.UseSandbox;
            przelewy24PaymentSettings.Description = model.Description;
            przelewy24PaymentSettings.TransferLabel = model.TransferLabel;
            przelewy24PaymentSettings.PaymentTimeLimit = model.PaymentTimeLimit;
            przelewy24PaymentSettings.AdditionalFee = model.AdditionalFee;
            przelewy24PaymentSettings.AdditionalFeePercentage = model.AdditionalFeePercentage;

            string sign = Md5Hash($"{model.MerchantId}|{model.CRC}");
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("p24_pos_id", model.MerchantId),
                new KeyValuePair<string, string>("p24_sign", sign)
            });

            string response;
            string baseUrl = przelewy24PaymentSettings.UseSandbox ? "sandbox" : "secure";

            using (var httpClient = new HttpClient())
            {
                httpClient.BaseAddress = new Uri($"https://{baseUrl}.przelewy24.pl");
                response = httpClient.PostAsync("/testConnection", content).Result.Content.ReadAsStringAsync().Result;
            }

            if (response.Contains("error=0"))
            {
                _notificationService.SuccessNotification($"Twoje dane są poprawne ({response}) dla trybu {baseUrl}", true);
            }
            else
            {
                _notificationService.ErrorNotification($"Twoje dane są niepoprawne ({response}) dla trybu {baseUrl}", true);
                przelewy24PaymentSettings.CRC = "Błędny CRC";
            }

            _settingService.SaveSetting(przelewy24PaymentSettings);

            return await Configure();
        }

        public IActionResult Return(int id)
        {
            return RedirectToRoute("CheckoutCompleted", new { orderId = id });
        }

        [AllowAnonymous]
        [HttpPost("/PaymentPrzelewy24/Status")]
        public async Task<IActionResult> Status([FromBody]Przelewy24Notification notification)
        {
            var sessionId = notification.P24SessionId;
            var amount = notification.P24Amount.ToString();
            var orderId = notification.P24OrderId.ToString();
            var posId = notification.P24PosId.ToString();
            var merchantId = notification.P24MerchantId.ToString();
            var currency = notification.P24Currency;
            var statement = notification.P24Statement;
            var sign = notification.P24Sign;
            var method = notification.P24Method;
            //var sessionId = formCollection["p24_session_id"].ToString();
            //var amount = formCollection["p24_amount"].ToString();
            //var orderId = formCollection["p24_order_id"].ToString();
            //var posId = formCollection["p24_pos_id"].ToString();
            //var merchantId = formCollection["p24_merchant_id"].ToString();
            //var method = formCollection["p24_method"].ToString();
            //var statement = formCollection["p24_statement"].ToString();
            //var currency = formCollection["p24_currency"].ToString();
            //var sign = formCollection["p24_sign"].ToString();

            //var sessionId = notification.p24_session_id.ToString();
            //var amount = notification.p24_amount.ToString();
            //var orderId = notification.p24_order_id.ToString();
            //var posId = notification.p24_pos_id.ToString();
            //var merchantId = notification.p24_merchant_id.ToString();
            //var method = notification.p24_method.ToString();
            //var statement = notification.p24_statement.ToString();
            //var currency = notification.p24_currency.ToString();
            //var sign = notification.p24_sign.ToString();
            await _logger.InformationAsync($"Get form collection currency {currency} amount {amount} orderId {orderId}");
            await _logger.InformationAsync($"Get form collection sign {sign}");

            //load settings for a chosen store scope
            var storeScope = await _storeContext.GetActiveStoreScopeConfigurationAsync();
            var przelewy24PaymentSettings = await _settingService.LoadSettingAsync<Przelewy24PaymentSettings>(storeScope);

            string calculatedSign = Md5Hash($"{sessionId}|{orderId}|{amount}|{currency}|{przelewy24PaymentSettings.CRC}");

            var order = await _orderService.GetOrderByGuidAsync(new Guid(sessionId));
            if (order == null)
            {
                await _logger.ErrorAsync($"Zamówienie nie istnieje - GUID: {sessionId}");
                return Content("ERROR");
            }

            if (sign != calculatedSign)
            {
                await _logger.ErrorAsync($"Błąd sumy kontrolnej: otrzymano {sign}, obliczono {calculatedSign}");
                return Content("ERROR");
            }

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("p24_merchant_id", merchantId),
                new KeyValuePair<string, string>("p24_pos_id", posId),
                new KeyValuePair<string, string>("p24_session_id", sessionId),
                new KeyValuePair<string, string>("p24_amount", amount),
                new KeyValuePair<string, string>("p24_currency", currency),
                new KeyValuePair<string, string>("p24_order_id", orderId),
                new KeyValuePair<string, string>("p24_sign", sign)
            });

            string response;
            using (var httpClient = new HttpClient())
            {
                string baseUrl = przelewy24PaymentSettings.UseSandbox ? "sandbox" : "secure";
                httpClient.BaseAddress = new Uri($"https://{baseUrl}.przelewy24.pl");
                response = httpClient.PostAsync("/trnVerify", content).Result.Content.ReadAsStringAsync().Result;
            }

            if (response.Contains("error=0"))
            {
                if (_orderProcessingService.CanMarkOrderAsPaid(order))
                {
                    await _orderService.InsertOrderNoteAsync(new OrderNote
                    {
                        Note = $"Zamówienie opłacone przez Przelewy24. ID transakcji: {orderId}",
                        DisplayToCustomer = true,
                        CreatedOnUtc = DateTime.UtcNow,
                        OrderId = order.Id
                    });
                    order.AuthorizationTransactionCode = statement;
                    await _orderService.UpdateOrderAsync(order);
                    await _orderProcessingService.MarkOrderAsPaidAsync(order);
                    await _logger.ErrorAsync($"Zamówienie {order.Id} zostało opłacone przez Przelewy24.", null, await _customerService.GetCustomerByIdAsync(order.CustomerId));
                }
                else
                {
                    await _logger.ErrorAsync($"Nie można oznaczyć zamówienia {order.Id} jako opłacone.", null, await _customerService.GetCustomerByIdAsync(order.CustomerId));
                }
            }
            else
            {
                await _logger.ErrorAsync($"Błąd podczas weryfikacji transakcji Przelewy24: {response}", null, await _customerService.GetCustomerByIdAsync(order.CustomerId));
            }

            return Content("OK");
        }

        //[AllowAnonymous]
        //[HttpPost("Status")]
        //public JsonResult Status()
        //{
        //    // Request data
        //    var data = Request;

        //    _logger.Information($"Get form collection STATUS");
        //    // Keys request
        //    var formCollection = Request.Form.Keys;
        //    foreach (var key in formCollection)
        //    {
        //        _logger.Information($"Get form collection sign {Request.Form[key].ToString()}");
        //    }
        //    //var sessionId = formCollection["p24_session_id"].ToString();
        //    //var amount = formCollection["p24_amount"].ToString();
        //    //var orderId = formCollection["p24_order_id"].ToString();
        //    //var posId = formCollection["p24_pos_id"].ToString();
        //    //var merchantId = formCollection["p24_merchant_id"].ToString();
        //    //var method = formCollection["p24_method"].ToString();
        //    //var statement = formCollection["p24_statement"].ToString();
        //    //var currency = formCollection["p24_currency"].ToString();
        //    //var sign = formCollection["p24_sign"].ToString();

        //    //var sessionId = notification.p24_session_id.ToString();
        //    //var amount = notification.p24_amount.ToString();
        //    //var orderId = notification.p24_order_id.ToString();
        //    //var posId = notification.p24_pos_id.ToString();
        //    //var merchantId = notification.p24_merchant_id.ToString();
        //    //var method = notification.p24_method.ToString();
        //    //var statement = notification.p24_statement.ToString();
        //    //var currency = notification.p24_currency.ToString();
        //    //var sign = notification.p24_sign.ToString();
        //     //_logger.Information($"Get form collection currency {currency} amount {amount} orderId {orderId}");
        //     //_logger.Information($"Get form collection sign {sign}");

        //    return Json("OK");
        //}

        public static string Md5Hash(string input)
        {
            using (var md5 = MD5.Create())
            {
                var hashBytes = md5.ComputeHash(Encoding.Default.GetBytes(input));
                var sb = new StringBuilder();
                foreach (var b in hashBytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
