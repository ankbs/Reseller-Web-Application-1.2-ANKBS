﻿// -----------------------------------------------------------------------
// <copyright file="PayPalGateway.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.Store.PartnerCenter.CustomerPortal.BusinessLogic.Commerce.PaymentGateways
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Exceptions;
    using Models;
    using PayPal;
    using PayPal.Api;

    /// <summary>
    /// PayPal payment gateway implementation.
    /// </summary>
    public class PayPalGateway : DomainObject, IPaymentGateway
    {
        /// <summary>
        /// Maintains the payer id for the payment gateway. 
        /// </summary>
        private readonly string payerId;

        /// <summary>
        /// Maintains the payment id for the payment gateway.
        /// </summary>
        private readonly string paymentId;

        /// <summary>
        /// Maintains the description for this payment. 
        /// </summary>
        private readonly string paymentDescription;

        /// <summary>
        /// Initializes a new instance of the <see cref="PayPalGateway" /> class. 
        /// </summary>
        /// <param name="applicationDomain">The ApplicationDomain</param>        
        /// <param name="description">The description which will be added to the Payment Card authorization call.</param>
        public PayPalGateway(ApplicationDomain applicationDomain, string description) : base(applicationDomain)
        {            
            description.AssertNotEmpty(nameof(description));
            this.paymentDescription = description;

            this.payerId = string.Empty;
            this.paymentId = string.Empty;            
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PayPalGateway" /> class.
        /// </summary>
        /// <param name="applicationDomain">The ApplicationDomain</param>        
        /// <param name="payerId">The Payer Id.</param>
        /// <param name="paymentId">The Payment Id.</param>
        public PayPalGateway(ApplicationDomain applicationDomain, string payerId, string paymentId) : base(applicationDomain)
        {
            payerId.AssertNotEmpty(nameof(payerId));
            paymentId.AssertNotEmpty(nameof(paymentId));            

            this.payerId = payerId;
            this.paymentId = paymentId;
            this.paymentDescription = string.Empty;            
        }

        /// <summary>
        /// Validates payment configuration. 
        /// </summary>
        /// <param name="paymentConfig">The Payment configuration.</param>
        public static void ValidateConfiguration(PaymentConfiguration paymentConfig)
        {
            string[] supportedPaymentModes = { "sandbox", "live" };

            paymentConfig.AssertNotNull(nameof(paymentConfig));

            paymentConfig.ClientId.AssertNotEmpty(nameof(paymentConfig.ClientId));
            paymentConfig.ClientSecret.AssertNotEmpty(nameof(paymentConfig.ClientSecret));
            paymentConfig.AccountType.AssertNotEmpty(nameof(paymentConfig.AccountType));

            if (!supportedPaymentModes.Contains(paymentConfig.AccountType))
            {
                throw new PartnerDomainException(Resources.InvalidPaymentModeErrorMessage);
            }

            try
            {
                Dictionary<string, string> configMap = new Dictionary<string, string>();
                configMap.Add("clientId", paymentConfig.ClientId);
                configMap.Add("clientSecret", paymentConfig.ClientSecret);
                configMap.Add("mode", paymentConfig.AccountType);
                configMap.Add("connectionTimeout", "120000");

                string accessToken = new OAuthTokenCredential(configMap).GetAccessToken();
                var apiContext = new APIContext(accessToken);
            }
            catch (PayPalException paypalException)
            {                
                if (paypalException is IdentityException)
                {
                    // thrown when API Context couldn't be setup. 
                    IdentityException identityFailure = paypalException as IdentityException;
                    IdentityError failureDetails = identityFailure.Details;
                    if (failureDetails != null && failureDetails.error.ToLower() == "invalid_client")
                    {                        
                        throw new PartnerDomainException(ErrorCode.PaymentGatewayIdentityFailureDuringConfiguration).AddDetail("ErrorMessage", Resources.PaymentGatewayIdentityFailureDuringConfiguration);
                    }                    
                }

                // if this is not an identity exception rather some other issue. 
                throw new PartnerDomainException(ErrorCode.PaymentGatewayFailure).AddDetail("ErrorMessage", paypalException.Message);                            
            }
        }

        /// <summary>
        /// Creates Web Experience profile using portal branding and payment configuration. 
        /// </summary>
        /// <param name="paymentConfig">The Payment configuration.</param>
        /// <param name="brandConfig">The branding configuration.</param>
        /// <param name="countryIso2Code">The locale code used by the web experience profile. Example-US.</param>
        /// <returns>The created web experience profile id.</returns>
        public static string CreateWebExperienceProfile(PaymentConfiguration paymentConfig, BrandingConfiguration brandConfig, string countryIso2Code)
        {
            try
            {
                Dictionary<string, string> configMap = new Dictionary<string, string>();
                configMap.Add("clientId", paymentConfig.ClientId);
                configMap.Add("clientSecret", paymentConfig.ClientSecret);
                configMap.Add("mode", paymentConfig.AccountType);
                configMap.Add("connectionTimeout", "120000");

                string accessToken = new OAuthTokenCredential(configMap).GetAccessToken();
                var apiContext = new APIContext(accessToken);
                apiContext.Config = configMap;

                // Pickup logo & brand name from branding configuration.                  
                // create the web experience profile.                 
                var profile = new WebProfile
                {
                    name = Guid.NewGuid().ToString(),
                    presentation = new Presentation
                    {
                        brand_name = brandConfig.OrganizationName,                        
                        logo_image = brandConfig.HeaderImage?.ToString(),
                        locale_code = countryIso2Code
                    },
                    input_fields = new InputFields()
                    {
                        address_override = 1,
                        allow_note = false,
                        no_shipping = 1 
                    },
                    flow_config = new FlowConfig()
                    {
                        landing_page_type = "billing"
                    }
                };
                
                var createdProfile = profile.Create(apiContext);

                // Now that new experience profile is created hence delete the older one.  
                if (!string.IsNullOrWhiteSpace(paymentConfig.WebExperienceProfileId))
                {
                    try
                    {
                        WebProfile existingWebProfile = WebProfile.Get(apiContext, paymentConfig.WebExperienceProfileId);
                        existingWebProfile.Delete(apiContext);
                    }
                    catch
                    {
                    }
                }

                return createdProfile.id;
            }
            catch (PayPalException paypalException)
            {
                if (paypalException is IdentityException)
                {
                    // thrown when API Context couldn't be setup. 
                    IdentityException identityFailure = paypalException as IdentityException;
                    IdentityError failureDetails = identityFailure.Details;
                    if (failureDetails != null && failureDetails.error.ToLower() == "invalid_client")
                    {
                        throw new PartnerDomainException(ErrorCode.PaymentGatewayIdentityFailureDuringConfiguration).AddDetail("ErrorMessage", Resources.PaymentGatewayIdentityFailureDuringConfiguration);
                    }
                }

                // if this is not an identity exception rather some other issue. 
                throw new PartnerDomainException(ErrorCode.PaymentGatewayFailure).AddDetail("ErrorMessage", paypalException.Message);
            }
        }

        /// <summary>
        /// Creates a payment transaction and returns the PayPal generated payment URL. 
        /// </summary>
        /// <param name="redirectUrl">The redirect url for PayPal callback to web store portal.</param>                
        /// <param name="order">The order details for which payment needs to be made.</param>        
        /// <returns>Payment URL from PayPal.</returns>
        public async Task<string> GeneratePaymentUriAsync(string redirectUrl, OrderViewModel order)
        {
            string paypalRedirectUrl = string.Empty;

            redirectUrl.AssertNotEmpty(nameof(redirectUrl));
            order.AssertNotNull(nameof(order));

            APIContext apiContext = await this.GetAPIContextAsync();
            decimal paymentTotal = 0;

            // PayPal wouldnt manage decimal points for few countries (example Hungary & Japan). 
            string moneyFixedPointFormat = (Resources.Culture.NumberFormat.CurrencyDecimalDigits == 0) ? "F0" : "F";

            // Create itemlist and add item objects to it.
            var itemList = new ItemList() { items = new List<Item>() };
            foreach (var subscriptionItem in order.Subscriptions)
            {
                itemList.items.Add(new Item()
                {
                    name = subscriptionItem.SubscriptionName,
                    description = this.paymentDescription,
                    sku = subscriptionItem.SubscriptionId,
                    currency = this.ApplicationDomain.PortalLocalization.CurrencyCode,
                    price = subscriptionItem.SeatPrice.ToString(moneyFixedPointFormat, CultureInfo.InvariantCulture),
                    quantity = subscriptionItem.Quantity.ToString()
                });                
                paymentTotal += Math.Round(subscriptionItem.Quantity * subscriptionItem.SeatPrice, Resources.Culture.NumberFormat.CurrencyDecimalDigits);                
            }            

            string webExperienceId = string.Empty;
            apiContext.Config.TryGetValue("WebExperienceProfileId", out webExperienceId);

            Payment payment = new Payment()
            {
                intent = "authorize",
                payer = new Payer() { payment_method = "paypal" },
                experience_profile_id = webExperienceId, // if null its ok, PayPal will pick up the default settings based on PayPal client configuration.
                transactions = new List<Transaction>()
                {
                    new Transaction()
                    {
                        description = this.paymentDescription,
                        custom = string.Format(CultureInfo.InvariantCulture, "{0}#{1}", order.CustomerId, order.OperationType.ToString()),
                        item_list = itemList,
                        amount = new Amount()
                        {
                            currency = this.ApplicationDomain.PortalLocalization.CurrencyCode,
                            total = paymentTotal.ToString(moneyFixedPointFormat, CultureInfo.InvariantCulture)  
                        }
                    }
                },
                redirect_urls = new RedirectUrls()
                {
                    return_url = redirectUrl + "&payment=success",
                    cancel_url = redirectUrl + "&payment=failure"
                }
            };
            System.Diagnostics.Debug.WriteLine("Total Amount:" + paymentTotal.ToString("F", Resources.Culture));

            try
            {
                // CreatePayment function gives us the payment approval url
                // on which payer is redirected for paypal acccount payment
                var createdPayment = payment.Create(apiContext);

                // get links returned from paypal in response to Create function call
                var links = createdPayment.links.GetEnumerator();
                while (links.MoveNext())
                {
                    Links lnk = links.Current;
                    if (lnk.rel.ToLower().Trim().Equals("approval_url"))
                    {
                        paypalRedirectUrl = lnk.href;
                    }
                }

                return await Task.FromResult(paypalRedirectUrl);
            }
            catch (PayPalException ex)
            {
                this.ParsePayPalException(ex);
            }

            return await Task.FromResult(string.Empty);
        }

        /// <summary>
        /// Executes a PayPal payment.
        /// </summary>
        /// <returns>Capture string id.</returns>
        public async Task<string> ExecutePaymentAsync()
        {
            APIContext apiContext = await this.GetAPIContextAsync();
            try
            {
                Payment payment = new Payment() { id = this.paymentId };
                var paymentExecution = new PaymentExecution() { payer_id = this.payerId };
                var paymentResult = payment.Execute(apiContext, paymentExecution);

                if (paymentResult.state.ToLowerInvariant() == "approved")
                {
                    string authorizationCode = paymentResult.transactions[0].related_resources[0].authorization.id;
                    return await Task.FromResult(authorizationCode);
                }
            }
            catch (PayPalException ex)
            {
                this.ParsePayPalException(ex);
            }

            return await Task.FromResult(string.Empty);
        }

        /// <summary>
        /// Finalizes an authorized payment with PayPal.
        /// </summary>
        /// <param name="authorizationCode">The authorization code for the payment to capture.</param>
        /// <returns>A task.</returns>
        public async Task CaptureAsync(string authorizationCode)
        {
            string authorizationCurrency;
            string authorizationAmount;
            Authorization cardAuthorization = null;

            authorizationCode.AssertNotEmpty(nameof(authorizationCode));
            
            APIContext apiContext = await this.GetAPIContextAsync();

            // given the authorizationId. Lookup the authorization to find the amount. 
            try
            {
                cardAuthorization = Authorization.Get(apiContext, authorizationCode);
                authorizationCurrency = cardAuthorization.amount.currency;
                authorizationAmount = cardAuthorization.amount.total;

                // Setting 'is_final_capture' to true, all remaining funds held by the authorization will be released from the funding instrument.
                var capture = new Capture()
                {
                    amount = new Amount()
                    {
                        currency = authorizationCurrency,
                        total = authorizationAmount
                    },
                    is_final_capture = true
                };

                var responseCapture = cardAuthorization.Capture(apiContext, capture);
                await Task.FromResult(string.Empty);
            }
            catch (PayPalException ex)
            {
                this.ParsePayPalException(ex);
            }
        }

        /// <summary>
        /// Voids an authorized payment with PayPal.
        /// </summary>
        /// <param name="authorizationCode">The authorization code for the payment to void.</param>
        /// <returns>a Task</returns>
        public async Task VoidAsync(string authorizationCode)
        {
            authorizationCode.AssertNotEmpty(nameof(authorizationCode));

            // given the authorizationId string... Lookup the authorization to void it. 
            try
            {                
                APIContext apiContext = await this.GetAPIContextAsync();
                Authorization cardAuthorization = Authorization.Get(apiContext, authorizationCode);
                cardAuthorization.Void(apiContext);
                await Task.FromResult(string.Empty);
            }
            catch (PayPalException ex)
            {
                this.ParsePayPalException(ex);
            }
        }

        /// <summary>
        /// Retrieves the Order from a payment transaction.
        /// </summary>
        /// <returns>The Order for which payment was made.</returns>
        public async Task<OrderViewModel> GetOrderDetailsFromPaymentAsync()
        {
            OrderViewModel orderFromPayment = null;   
            APIContext apiContext = await this.GetAPIContextAsync();

            try
            {
                // the get will retrieve the payment information. iterate the items in the transaction collection to extract details.            
                Payment paymentDetails = Payment.Get(apiContext, this.paymentId);
                orderFromPayment = new OrderViewModel();                
                List<OrderSubscriptionItemViewModel> orderSubscriptions = new List<OrderSubscriptionItemViewModel>();

                if (paymentDetails.transactions.Count > 0)
                {
                    string customData = paymentDetails.transactions[0].custom;

                    // parse out the customer Id & operation type from customData.
                    string[] customDataArray = customData.Split("#".ToCharArray());
                    if (customDataArray.Length == 2)
                    {
                        orderFromPayment.CustomerId = customDataArray[0];
                        orderFromPayment.OperationType = (CommerceOperationType)Enum.Parse(typeof(CommerceOperationType), customDataArray[1], true);
                    }

                    foreach (var paymentTransactionItem in paymentDetails.transactions[0].item_list.items)
                    {
                        orderSubscriptions.Add(new OrderSubscriptionItemViewModel()
                        {   
                            SubscriptionId = paymentTransactionItem.sku,
                            OfferId = paymentTransactionItem.sku,
                            Quantity = Convert.ToInt32(paymentTransactionItem.quantity, CultureInfo.InvariantCulture) 
                        });
                    }
                }

                orderFromPayment.Subscriptions = orderSubscriptions;
            }
            catch (PayPalException ex)
            {
                this.ParsePayPalException(ex);
            }

            return await Task.FromResult(orderFromPayment);
        }

        /// <summary>
        /// Retrieves the API Context for PayPal. 
        /// </summary>
        /// <returns>PayPal APIContext</returns>
        private async Task<APIContext> GetAPIContextAsync()
        {
            //// The GetAccessToken() of the SDK Returns the currently cached access token. 
            //// If no access token was previously cached, or if the current access token is expired, then a new one is generated and returned. 
            //// See more - https://github.com/paypal/PayPal-NET-SDK/blob/develop/Source/SDK/Api/OAuthTokenCredential.cs

            // Before getAPIContext ... set up PayPal configuration. This is an expensive call which can benefit from caching. 
            PaymentConfiguration paymentConfig = await ApplicationDomain.Instance.PaymentConfigurationRepository.RetrieveAsync();

            Dictionary<string, string> configMap = new Dictionary<string, string>();
            configMap.Add("clientId", paymentConfig.ClientId);
            configMap.Add("clientSecret", paymentConfig.ClientSecret);
            configMap.Add("mode", paymentConfig.AccountType);
            configMap.Add("WebExperienceProfileId", paymentConfig.WebExperienceProfileId);            
            configMap.Add("connectionTimeout", "120000");            

            string accessToken = new OAuthTokenCredential(configMap).GetAccessToken();
            var apiContext = new APIContext(accessToken);
            apiContext.Config = configMap;
            return apiContext;
        }

        /// <summary>
        /// Throws PartnerDomainException by parsing PayPal exception. 
        /// </summary>
        /// <param name="ex">Exceptions from PayPal SDK.</param>        
        private void ParsePayPalException(PayPalException ex)
        {
            if (ex is PaymentsException)
            {
                PaymentsException pe = ex as PaymentsException;

                // Get the details of this exception with ex.Details and format the error message in the form of "We are unable to process your payment –  {Errormessage} :: [err1, err2, .., errN]".                
                StringBuilder errorString = new StringBuilder();
                errorString.Append(Resources.PaymentGatewayErrorPrefix);                

                // build error string for errors returned from financial institutions.
                if (pe.Details != null)
                {
                    string errorName = pe.Details.name.ToUpper();

                    if (errorName == null || errorName.Length < 1)
                    {
                        errorString.Append(pe.Details.message);
                        throw new PartnerDomainException(ErrorCode.PaymentGatewayFailure).AddDetail("ErrorMessage", errorString.ToString());
                    }                        
                    else if (errorName.Contains("UNKNOWN_ERROR"))
                    {                        
                        throw new PartnerDomainException(ErrorCode.PaymentGatewayPaymentError);
                    }
                    else if (errorName.Contains("VALIDATION") && pe.Details.details != null)
                    {
                        // Check if there are sub collection details and build error string.                                       
                        errorString.Append("[");
                        foreach (ErrorDetails errorDetails in pe.Details.details)
                        {
                            // removing extrataneous information.                     
                            string errorField = errorDetails.field;
                            if (errorField.Contains("payer.funding_instruments[0]."))
                            {
                                errorField = errorField.Replace("payer.funding_instruments[0].", string.Empty).ToString();
                            }

                            errorString.AppendFormat("{0} - {1},", errorField, errorDetails.issue);
                        }

                        errorString.Replace(',', ']', errorString.Length - 2, 2); // remove the last comma and replace it with ]. 
                    }
                    else
                    {                        
                        errorString.Append(Resources.PayPalUnableToProcessPayment);
                    }
                }

                throw new PartnerDomainException(ErrorCode.PaymentGatewayFailure).AddDetail("ErrorMessage", errorString.ToString());
            }

            if (ex is IdentityException)
            {
                // ideally this shouldn't be raised from customer experience calls. 
                // can occur when admin has generated a new secret for an existing app id in PayPal but didnt update portal payment configuration.                                
                throw new PartnerDomainException(ErrorCode.PaymentGatewayIdentityFailureDuringPayment).AddDetail("ErrorMessage", Resources.PaymentGatewayIdentityFailureDuringPayment);
            }

            // few PayPalException types contain meaningfull exception information only in InnerException. 
            if (ex is PayPalException && ex.InnerException != null)
            {                
                throw new PartnerDomainException(ErrorCode.PaymentGatewayFailure).AddDetail("ErrorMessage", ex.InnerException.Message);
            }
            else
            {                
                throw new PartnerDomainException(ErrorCode.PaymentGatewayFailure).AddDetail("ErrorMessage", ex.Message);
            }
        }
    }
}