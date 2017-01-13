﻿// -----------------------------------------------------------------------
// <copyright file="AzureStorageService.cs" company="Microsoft">
//      Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Microsoft.Store.PartnerCenter.CustomerPortal.BusinessLogic
{
    using System;
    using System.Configuration;
    using System.Globalization;
    using System.Threading.Tasks;
    using WindowsAzure.Storage;
    using WindowsAzure.Storage.Blob;
    using WindowsAzure.Storage.Table;

    /// <summary>
    /// Provides Azure storage assets.
    /// </summary>
    public class AzureStorageService
    {
        /// <summary>
        /// The name of the portal assets blob container.
        /// </summary>
        private const string PrivatePortalAssetsBlobContainerName = "customerportalassets";

        /// <summary>
        /// The name of the portal asserts blob container which contains publicly available blobs.
        /// This is useful for storing images which the browser can access.
        /// </summary>
        private const string PublicPortalAssetsBlobContainerName = "publiccustomerportalassets";

        /// <summary>
        /// The name of the Partner Center customers Azure table.
        /// </summary>
        private const string CustomersTableName = "PartnerCenterCustomers";

        /// <summary>
        /// The name of the customer subscriptions Azure table.
        /// </summary>
        private const string CustomerSubscriptionsTableName = "CustomerSubscriptions";

        /// <summary>
        /// The name of the customer purchases Azure table.
        /// </summary>
        private const string CustomerPurchasesTableName = "CustomerPurchases";

        /// <summary>
        /// The Azure cloud storage account.
        /// </summary>
        private CloudStorageAccount storageAccount;

        /// <summary>
        /// The BLOB container which contains the portal's configuration assets.
        /// </summary>
        private CloudBlobContainer privateBlobContainer;

        /// <summary>
        /// The BLOB container which contains the public portal's assets.
        /// </summary>
        private CloudBlobContainer publicBlobContainer;

        /// <summary>
        /// The Azure partner center customers table.
        /// </summary>
        private CloudTable partnerCenterCustomersTable;

        /// <summary>
        /// The Azure customer subscriptions table.
        /// </summary>
        private CloudTable customerSubscriptionsTable;

        /// <summary>
        /// The Azure customer purchases table.
        /// </summary>
        private CloudTable customerPurchasesTable;

        /// <summary>
        /// Initializes a new instance of the <see cref="AzureStorageService"/> class.
        /// </summary>
        /// <param name="azureStorageConnectionString">The Azure storage connection string required to access the customer portal assets.</param>
        /// <param name="azureStorageConnectionEndpointSuffix">The Azure storage connection endpoint suffix.</param>
        public AzureStorageService(string azureStorageConnectionString, string azureStorageConnectionEndpointSuffix)
        {
            azureStorageConnectionString.AssertNotEmpty(nameof(azureStorageConnectionString));
            azureStorageConnectionEndpointSuffix.AssertNotEmpty(nameof(azureStorageConnectionEndpointSuffix));

            CloudStorageAccount cloudStorageAccount;           
            if (CloudStorageAccount.TryParse(azureStorageConnectionString, out cloudStorageAccount))
            {
                this.storageAccount = new CloudStorageAccount(cloudStorageAccount.Credentials, endpointSuffix: azureStorageConnectionEndpointSuffix, useHttps: true);
            }
            else
            {
                throw new ConfigurationErrorsException("webPortal.azureStorageConnectionString setting not valid in web.config");
            }
        }

        /// <summary>
        /// Generates a new BLOB reference to store a new asset.
        /// </summary>
        /// <param name="blobContainer">The Blob container in which to create the BLOB.</param>
        /// <param name="blobPrefix">The BLOB name prefix to use.</param>
        /// <returns>The new BLOB reference.</returns>
        public async Task<CloudBlockBlob> GenerateNewBlobReferenceAsync(CloudBlobContainer blobContainer, string blobPrefix)
        {
            blobContainer.AssertNotNull(nameof(blobContainer));
            
            blobPrefix = blobPrefix ?? "asset";
            const string BlobNameFormat = "{0}{1}";
            CloudBlockBlob newBlob = null;

            do
            {
                newBlob = blobContainer.GetBlockBlobReference(string.Format(
                    CultureInfo.InvariantCulture,
                    BlobNameFormat,
                    blobPrefix,
                    new Random().Next().ToString()));
            }
            while (await newBlob.ExistsAsync());

            return newBlob;
        }

        /// <summary>
        /// Returns a cloud BLOB container reference which can be used to manage the customer portal assets.
        /// </summary>
        /// <returns>The customer portal assets BLOB container.</returns>
        public async Task<CloudBlobContainer> GetPrivateCustomerPortalAssetsBlobContainerAsync()
        {
            if (this.privateBlobContainer == null)
            {
                CloudBlobClient blobClient = this.storageAccount.CreateCloudBlobClient();
                this.privateBlobContainer = blobClient.GetContainerReference(AzureStorageService.PrivatePortalAssetsBlobContainerName);
            }

            await this.privateBlobContainer.CreateIfNotExistsAsync();
            return this.privateBlobContainer;
        }

        /// <summary>
        /// Returns a cloud BLOB container reference which can be used to manage the public customer portal assets.
        /// </summary>
        /// <returns>The public customer portal assets BLOB container.</returns>
        public async Task<CloudBlobContainer> GetPublicCustomerPortalAssetsBlobContainerAsync()
        {
            if (this.publicBlobContainer == null)
            {
                CloudBlobClient blobClient = this.storageAccount.CreateCloudBlobClient();
                this.publicBlobContainer = blobClient.GetContainerReference(AzureStorageService.PublicPortalAssetsBlobContainerName);                
            }

            if (!await this.publicBlobContainer.ExistsAsync())
            {
                await this.publicBlobContainer.CreateAsync();

                var permissions = await this.publicBlobContainer.GetPermissionsAsync();
                permissions.PublicAccess = BlobContainerPublicAccessType.Blob;

                await this.publicBlobContainer.SetPermissionsAsync(permissions);
            }

            return this.publicBlobContainer;
        }

        /// <summary>
        /// Gets the Partner Center customers table.
        /// </summary>
        /// <returns>The Partner Center customers table.</returns>
        public async Task<CloudTable> GetPartnerCenterCustomersTableAsync()
        {
            if (this.partnerCenterCustomersTable == null)
            {                
                CloudTableClient tableClient = this.storageAccount.CreateCloudTableClient();
                this.partnerCenterCustomersTable = tableClient.GetTableReference(AzureStorageService.CustomersTableName);
            }

            // someone can delete the table externally
            await this.partnerCenterCustomersTable.CreateIfNotExistsAsync();
            return this.partnerCenterCustomersTable;
        }

        /// <summary>
        /// Gets the customer subscriptions table.
        /// </summary>
        /// <returns>The customer subscriptions table.</returns>
        public async Task<CloudTable> GetCustomerSubscriptionsTableAsync()
        {
            if (this.customerSubscriptionsTable == null)
            {
                CloudTableClient tableClient = this.storageAccount.CreateCloudTableClient();
                this.customerSubscriptionsTable = tableClient.GetTableReference(AzureStorageService.CustomerSubscriptionsTableName);
            }

            // someone can delete the table externally
            await this.customerSubscriptionsTable.CreateIfNotExistsAsync();
            return this.customerSubscriptionsTable;
        }

        /// <summary>
        /// Gets the customer purchases table.
        /// </summary>
        /// <returns>The customer purchases table.</returns>
        public async Task<CloudTable> GetCustomerPurchasesTableAsync()
        {
            if (this.customerPurchasesTable == null)
            {
                CloudTableClient tableClient = this.storageAccount.CreateCloudTableClient();
                this.customerPurchasesTable = tableClient.GetTableReference(AzureStorageService.CustomerPurchasesTableName);
            }

            // someone can delete the table externally
            await this.customerPurchasesTable.CreateIfNotExistsAsync();
            return this.customerPurchasesTable;
        }
    }
}