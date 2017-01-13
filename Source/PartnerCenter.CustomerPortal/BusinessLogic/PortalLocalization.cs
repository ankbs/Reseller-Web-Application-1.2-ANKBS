// -----------------------------------------------------------------------
// <copyright file="PortalLocalization.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.Store.PartnerCenter.CustomerPortal.BusinessLogic
{
    using System.Globalization;
    using System.Linq;
    using System.Threading.Tasks;

    /// <summary>
    /// Encapsulates locale information for the portal based on the partner's region.
    /// </summary>
    public class PortalLocalization : DomainObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PortalLocalization"/> class.
        /// </summary>
        /// <param name="applicationDomain">An application domain instance.</param>
        public PortalLocalization(ApplicationDomain applicationDomain) : base(applicationDomain)
        {
        }

        /// <summary>
        /// Gets the portal's country ISO2 code. E.g. US
        /// </summary>
        public string CountryIso2Code { get; private set; }

        /// <summary>
        /// Gets the portal's locale. E.g. En-US
        /// </summary>
        public string Locale { get; private set; }

        /// <summary>
        /// Gets the portal's ISO currency code. E.g. USD
        /// </summary>
        public string CurrencyCode { get; private set; }

        /// <summary>
        /// Gets the portal's currency symbol. E.g. $
        /// </summary>
        public string CurrencySymbol { get; private set; }

        /// <summary>
        /// Initializes state and ensures the object is ready to be consumed.
        /// </summary>
        /// <returns>A task.</returns>
        public async Task InitializeAsync()
        {
            var partnerLegalBusinessProfile = await this.ApplicationDomain.PartnerCenterClient.Profiles.LegalBusinessProfile.GetAsync();
            this.CountryIso2Code = partnerLegalBusinessProfile.Address.Country;

            RegionInfo partnerRegion = null;
            
            try
            {   
                // Get the default locale using the Country Validation rules infrastructure.  
                var partnerCountryValidationRules = await ApplicationDomain.Instance.PartnerCenterClient.CountryValidationRules.ByCountry(this.CountryIso2Code).GetAsync();

                this.Locale = partnerCountryValidationRules.DefaultCulture;
                partnerRegion = new RegionInfo(new CultureInfo(this.Locale, false).LCID);
            }
            catch
            {
                // we will default region to en-US so that currency is USD. 
                this.Locale = "en-US";
                partnerRegion = new RegionInfo(new CultureInfo(this.Locale, false).LCID);                
            }
            
            // figure out the currency             
            this.CurrencyCode = partnerRegion.ISOCurrencySymbol;
            this.CurrencySymbol = partnerRegion.CurrencySymbol;

            // set culture to partner locale.
            Resources.Culture = new CultureInfo(this.Locale);
        }
    }
}