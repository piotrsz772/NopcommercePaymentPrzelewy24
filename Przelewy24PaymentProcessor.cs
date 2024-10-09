using Microsoft.AspNetCore.Http;
using Nop.Core;
using Nop.Core.Domain;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Messages;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Payments;
using Nop.Core.Domain.Stores;
using Nop.Plugin.Payments.Przelewy24.Components;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Messages;
using Nop.Services.Orders;
using Nop.Services.Payments;
using Nop.Services.Plugins;
using Nop.Web.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Nop.Plugin.Payments.Przelewy24
{
    public class Przelewy24PaymentProcessor : BasePlugin, IPaymentMethod
    {
        private static string pn = "Przelewy24";
        private readonly Przelewy24PaymentSettings _przelewy24PaymentSettings;
        private readonly StoreInformationSettings _storeInformationSettings;
        private readonly ICurrencyService _currencyService;
        private readonly CurrencySettings _currencySettings;
        private readonly ISettingService _settingService;
        private readonly IWebHelper _webHelper;
        private readonly IWorkContext _workContext;
        private readonly ILogger _logger;
        private readonly IOrderTotalCalculationService _orderTotalCalculationService;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILocalizationService _localizationService;
        private readonly IPaymentService _paymentService;
        private readonly IWorkflowMessageService _workflowMessageService;
        private readonly IMessageTemplateService _messageTemplateService;
        private readonly IEmailAccountService _emailAccountService;
        private readonly EmailAccountSettings _emailAccountSettings;
        private readonly IMessageTokenProvider _messageTokenProvider;
        private readonly IStoreContext _storeContext;
        private readonly IAddressService _addressService;
        private readonly ICountryService _countryService;

        public Przelewy24PaymentProcessor(
            Przelewy24PaymentSettings przelewy24PaymentSettings,
            StoreInformationSettings storeInformationSettings,
            ICurrencyService currencyService,
            CurrencySettings currencySettings,
            ISettingService settingService,
            IWebHelper webHelper,
            IWorkContext workContext,
            ILogger logger,
            IOrderTotalCalculationService orderTotalCalculationService,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService,
            IPaymentService paymentService,
            IWorkflowMessageService workflowMessageService,
            IMessageTemplateService messageTemplateService,
            IEmailAccountService emailAccountService,
            EmailAccountSettings emailAccountSettings,
            IMessageTokenProvider messageTokenProvider,
            IStoreContext storeContext,
            IAddressService addressService,
            ICountryService countryService)
        {
            _przelewy24PaymentSettings = przelewy24PaymentSettings;
            _storeInformationSettings = storeInformationSettings;
            _currencyService = currencyService;
            _currencySettings = currencySettings;
            _settingService = settingService;
            _webHelper = webHelper;
            _workContext = workContext;
            _logger = logger;
            _orderTotalCalculationService = orderTotalCalculationService;
            _httpContextAccessor = httpContextAccessor;
            _localizationService = localizationService;
            _paymentService = paymentService;
            _workflowMessageService = workflowMessageService;
            _messageTemplateService = messageTemplateService;
            _emailAccountService = emailAccountService;
            _emailAccountSettings = emailAccountSettings;
            _messageTokenProvider = messageTokenProvider;
            _storeContext = storeContext;
            _addressService = addressService;
            _countryService = countryService;
        }

        public async Task PostProcessPayment(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var form = await CreateForm(postProcessPaymentRequest);
            RegisterAndRequestTransaction(form);
        }

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

        private async Task<FormUrlEncodedContent> CreateForm(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var order = postProcessPaymentRequest.Order;
            var billingAddress = await _addressService.GetAddressByIdAsync(order.BillingAddressId);

            string firstName = billingAddress.FirstName ?? "";
            string lastName = billingAddress.LastName ?? "";
            string address = billingAddress.Address1 ?? "";
            string city = billingAddress.City ?? "";
            string phone = billingAddress.PhoneNumber ?? "";
            string zipCode = billingAddress.ZipPostalCode ?? "";
            string country = (await _countryService.GetCountryByIdAsync(billingAddress.CountryId.Value))?.TwoLetterIsoCode ?? "";

            string amount = (await _currencyService.ConvertFromPrimaryStoreCurrencyAsync(order.OrderTotal, await _workContext.GetWorkingCurrencyAsync()) * 100M).ToString("F0");
            string sessionId = order.OrderGuid.ToString();
            string storeLocation = _webHelper.GetStoreLocation(false);
            string orderId = order.Id.ToString();
            string returnUrl = $"{storeLocation}PaymentPrzelewy24/Return/{orderId}";
            string statusUrl = $"{storeLocation}PaymentPrzelewy24/Status";

            var sign = Md5Hash($"{sessionId}|{_przelewy24PaymentSettings.MerchantId}|{amount}|{order.CustomerCurrencyCode}|{_przelewy24PaymentSettings.CRC}");
            var parameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("p24_merchant_id", _przelewy24PaymentSettings.MerchantId),
                new KeyValuePair<string, string>("p24_pos_id", _przelewy24PaymentSettings.MerchantId),
                new KeyValuePair<string, string>("p24_session_id", sessionId),
                new KeyValuePair<string, string>("p24_amount", amount),
                new KeyValuePair<string, string>("p24_currency", order.CustomerCurrencyCode),
                new KeyValuePair<string, string>("p24_description", $"{_przelewy24PaymentSettings.Description} [{order.Id}]"),
                new KeyValuePair<string, string>("p24_email", billingAddress.Email),
                new KeyValuePair<string, string>("p24_client", $"{firstName} {lastName}"),
                new KeyValuePair<string, string>("p24_address", address),
                new KeyValuePair<string, string>("p24_zip", zipCode),
                new KeyValuePair<string, string>("p24_city", city),
                new KeyValuePair<string, string>("p24_country", country),
                new KeyValuePair<string, string>("p24_phone", phone),
                new KeyValuePair<string, string>("p24_language", await SelectLanguage()),
                new KeyValuePair<string, string>("p24_url_return", returnUrl),
                new KeyValuePair<string, string>("p24_url_status", statusUrl),
                new KeyValuePair<string, string>("p24_time_limit", _przelewy24PaymentSettings.PaymentTimeLimit.ToString()),
                new KeyValuePair<string, string>("p24_transfer_label", _przelewy24PaymentSettings.TransferLabel),
                new KeyValuePair<string, string>("p24_api_version", "3.2"),
                new KeyValuePair<string, string>("p24_sign", sign),
                new KeyValuePair<string, string>("p24_encoding", "ISO-8859-2")
            };

            _logger.Information($"CreateForm returnUrl: {returnUrl}");
            _logger.Information($"CreateForm statusUrl: {statusUrl}");
            _logger.Information($"CreateForm sign: {sign}");

            return new FormUrlEncodedContent(parameters);
        }

        private void RegisterAndRequestTransaction(FormUrlEncodedContent form)
        {
            string baseUrl = _przelewy24PaymentSettings.UseSandbox ? "sandbox" : "secure";
            string responseContent;

            using (var httpClient = new HttpClient { BaseAddress = new Uri($"https://{baseUrl}.przelewy24.pl") })
            {
                var response = httpClient.PostAsync("/trnRegister", form).Result;
                responseContent = response.Content.ReadAsStringAsync().Result;
            }

            if (!responseContent.Contains("error=0"))
                throw new InvalidOperationException(responseContent);

            var tokenIndex = responseContent.IndexOf("token=") + 6;
            var token = responseContent.Substring(tokenIndex);

            var redirectUrl = $"https://{baseUrl}.przelewy24.pl/trnRequest/{token}";
            _httpContextAccessor.HttpContext.Response.Redirect(redirectUrl);
        }

        private async Task<string> SelectLanguage()
        {
            var languageCode = (await _workContext.GetWorkingLanguageAsync()).UniqueSeoCode.ToLowerInvariant();
            switch (languageCode)
            {
                case "pl":
                    return "PL";
                case "en":
                    return "EN";
                case "de":
                    return "DE";
                case "es":
                    return "ES";
                case "it":
                    return "IT";
                default:
                    return "EN";
            }
        }

        public override async Task InstallAsync()
        {
            //settings
            var settings = new Przelewy24PaymentSettings
            {
                MerchantId = "00000",
                CRC = "000001ab43d0c70",
                UseSandbox = true,
                Description = "Order No. ",
                TransferLabel = "Przelewy24 1.0",
                PaymentTimeLimit = 5,
                AdditionalFee = 0M,
                AdditionalFeePercentage = false
            };
            await _settingService.SaveSettingAsync(settings);

            //locales
            await _localizationService.AddOrUpdateLocaleResourceAsync(new Dictionary<string, string>
            {
                ["Plugins.Payments.Przelewy24.Fields.MerchantId"] = "Merchant Id",
                ["Plugins.Payments.Przelewy24.Fields.MerchantId.Hint"] = "Dostępny tutaj: secure.przelewy24.pl /panel/sprzedawca.php",
                ["Plugins.Payments.Przelewy24.Fields.CRC"] = "CRC",
                ["Plugins.Payments.Przelewy24.Fields.CRC.Hint"] = "Dostępny tutaj: secure.przelewy24.pl /panel/sprzedawca.php",
                ["Plugins.Payments.Przelewy24.Fields.UseSandbox"] = "Używaj Sandbox",
                ["Plugins.Payments.Przelewy24.Fields.Description"] = "Opis",
                ["Plugins.Payments.Przelewy24.Fields.Description.Hint"] = "Opis zamówienia pojawi się tutaj: secure.przelewy24.pl /panel/transakcja.php",
                ["Plugins.Payments.Przelewy24.Fields.TransferLabel"] = "Tekst transakcji",
                ["Plugins.Payments.Przelewy24.Fields.TransferLabel.Hint"] = "Tekst, który będzie widoczny w trakcie procesu płatności.",
                ["Plugins.Payments.Przelewy24.Fields.PaymentTimeLimit"] = "Limit czasowy transakcji",
                ["Plugins.Payments.Przelewy24.Fields.PaymentTimeLimit.Hint"] = "Limit czasowy w którym klient może dokonać płatności",
                ["Plugins.Payments.Przelewy24.Fields.AdditionalFee"] = "Dodatkowa opłata",
                ["Plugins.Payments.Przelewy24.Fields.AdditionalFeePercentage"] = "Dodatkowa opłata w procentach",
                ["Plugins.Payments.Przelewy24.PaymentMethodDescription"] = "Opis dodatkowy"
            });

            await CreateMessageTemplate();

            var messageTemplate = (await _messageTemplateService.GetMessageTemplatesByNameAsync("Plugin.Installation." + pn)).FirstOrDefault();
            if (messageTemplate != null)
            {
                await SendInstallNotification();
            }


            await base.InstallAsync();
        }

        public override async Task UninstallAsync()
        {
            //settings
            await _settingService.DeleteSettingAsync<Przelewy24PaymentSettings>();

            //locales
            await _localizationService.DeleteLocaleResourcesAsync("Plugins.Payments.Przelewy24");

            await SendUninstallNotification();

            await base.UninstallAsync();
        }

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/PaymentPrzelewy24/Configure";
        }

        public bool SupportCapture => false;
        public bool SupportPartiallyRefund => false;
        public bool SupportRefund => false;
        public bool SupportVoid => false;
        public RecurringPaymentType RecurringPaymentType => RecurringPaymentType.NotSupported;
        public PaymentMethodType PaymentMethodType => PaymentMethodType.Redirection;
        public bool SkipPaymentInfo => true;

        private async Task CreateMessageTemplate()
        {
            var emailAccountId = _emailAccountSettings.DefaultEmailAccountId;

            var installTemplate = new MessageTemplate
            {
                Name = "Plugin.Installation." + pn,
                Subject = "Plugin installation notification",
                Body = $"<p><a href=\"%Store.URL%\">%Store.Name%</a></p><p>Plugin {pn} has been installed</p>",
                IsActive = true,
                EmailAccountId = emailAccountId,
                LimitedToStores = false
            };
            await _messageTemplateService.InsertMessageTemplateAsync(installTemplate);

            var uninstallTemplate = new MessageTemplate
            {
                Name = "Plugin.UnInstallation." + pn,
                Subject = "Plugin uninstallation notification",
                Body = $"<p><a href=\"%Store.URL%\">%Store.Name%</a></p><p>Plugin {pn} has been uninstalled</p>",
                IsActive = true,
                EmailAccountId = emailAccountId,
                LimitedToStores = false
            };
            await _messageTemplateService.InsertMessageTemplateAsync(uninstallTemplate);
        }

        public async Task<int> SendInstallNotification()
        {
            var messageTemplate = (await _messageTemplateService.GetMessageTemplatesByNameAsync("Plugin.Installation." + pn)).FirstOrDefault();
            if (messageTemplate == null)
                return 0;

            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(messageTemplate.EmailAccountId);
            var store = await _storeContext.GetCurrentStoreAsync();
            var tokens = new List<Token>();
            await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount);

            await _workflowMessageService.SendNotificationAsync(messageTemplate, emailAccount, 0, tokens, "biuro@playdev.pl", "Support");

            return 0;
        }

        public async Task<int> SendUninstallNotification()
        {
            var messageTemplate = (await _messageTemplateService.GetMessageTemplatesByNameAsync("Plugin.UnInstallation." + pn)).FirstOrDefault();
            if (messageTemplate == null)
                return 0;

            var emailAccount = await _emailAccountService.GetEmailAccountByIdAsync(messageTemplate.EmailAccountId);
            var store = await _storeContext.GetCurrentStoreAsync();
            var tokens = new List<Token>();
            await _messageTokenProvider.AddStoreTokensAsync(tokens, store, emailAccount);

            await _workflowMessageService.SendNotificationAsync(messageTemplate, emailAccount, 0, tokens, "biuro@playdev.pl", "Support");

            return 0;
        }

        public Task<ProcessPaymentResult> ProcessPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult
            { 
                NewPaymentStatus = PaymentStatus.Pending
            });
        }

        public async Task PostProcessPaymentAsync(PostProcessPaymentRequest postProcessPaymentRequest)
        {
            var form = await CreateForm(postProcessPaymentRequest);
            RegisterAndRequestTransaction(form);
        }

        public async Task<bool> HidePaymentMethodAsync(IList<ShoppingCartItem> cart)
        {
            return await Task.FromResult(false);
        }

        public async Task<decimal> GetAdditionalHandlingFeeAsync(IList<ShoppingCartItem> cart)
        {
            return await _orderTotalCalculationService.CalculatePaymentAdditionalFeeAsync(cart,
                _przelewy24PaymentSettings.AdditionalFee, _przelewy24PaymentSettings.AdditionalFeePercentage);
        }

        /// <summary>
        /// Captures payment
        /// </summary>
        /// <param name="capturePaymentRequest">Capture payment request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the capture payment result
        /// </returns>
        public Task<CapturePaymentResult> CaptureAsync(CapturePaymentRequest capturePaymentRequest)
        {
            return Task.FromResult(new CapturePaymentResult { Errors = new[] { "Capture method not supported" } });
        }

        /// <summary>
        /// Refunds a payment
        /// </summary>
        /// <param name="refundPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<RefundPaymentResult> RefundAsync(RefundPaymentRequest refundPaymentRequest)
        {
            return Task.FromResult(new RefundPaymentResult { Errors = new[] { "Refund method not supported" } });
        }

        /// <summary>
        /// Voids a payment
        /// </summary>
        /// <param name="voidPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<VoidPaymentResult> VoidAsync(VoidPaymentRequest voidPaymentRequest)
        {
            return Task.FromResult(new VoidPaymentResult { Errors = new[] { "Void method not supported" } });
        }

        /// <summary>
        /// Process recurring payment
        /// </summary>
        /// <param name="processPaymentRequest">Payment info required for an order processing</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the process payment result
        /// </returns>
        public Task<ProcessPaymentResult> ProcessRecurringPaymentAsync(ProcessPaymentRequest processPaymentRequest)
        {
            return Task.FromResult(new ProcessPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Cancels a recurring payment
        /// </summary>
        /// <param name="cancelPaymentRequest">Request</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<CancelRecurringPaymentResult> CancelRecurringPaymentAsync(CancelRecurringPaymentRequest cancelPaymentRequest)
        {
            return Task.FromResult(new CancelRecurringPaymentResult { Errors = new[] { "Recurring payment not supported" } });
        }

        /// <summary>
        /// Gets a value indicating whether customers can complete a payment after order is placed but not completed (for redirection payment methods)
        /// </summary>
        /// <param name="order">Order</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the result
        /// </returns>
        public Task<bool> CanRePostProcessPaymentAsync(Order order)
        {
            ArgumentNullException.ThrowIfNull(order);

            //it's not a redirection payment method. So we always return false
            return Task.FromResult(false);
        }

        /// <summary>
        /// Validate payment form
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the list of validating errors
        /// </returns>
        public Task<IList<string>> ValidatePaymentFormAsync(IFormCollection form)
        {
            return Task.FromResult<IList<string>>(new List<string>());
        }

        /// <summary>
        /// Get payment information
        /// </summary>
        /// <param name="form">The parsed form values</param>
        /// <returns>
        /// A task that represents the asynchronous operation
        /// The task result contains the payment info holder
        /// </returns>
        public Task<ProcessPaymentRequest> GetPaymentInfoAsync(IFormCollection form)
        {
            return Task.FromResult(new ProcessPaymentRequest());
        }

        public Type GetPublicViewComponent()
        {
            return typeof(PaymentPrzelewy24PaymentInfoViewComponent);
        }

        public async Task<string> GetPaymentMethodDescriptionAsync()
        {
            return await _localizationService.GetResourceAsync("Plugins.Payments.Przelewy24.PaymentMethodDescription");
        }
    }
}
