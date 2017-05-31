using Microsoft.WindowsAzure.ServiceRuntime;
using Microsoft.WindowsAzure.Storage.Blob;
using Sitecore.Azure.Configuration;
using Sitecore.Azure.Deployments;
using Sitecore.Azure.Deployments.StorageProjects;
using Sitecore.Azure.Managers;
using Sitecore.Azure.Managers.AzureManagers;
using Sitecore.Azure.Managers.Publishing;
using Sitecore.Azure.Sys.Data;
using Sitecore.Configuration;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Diagnostics;
using Sitecore.IO;
using Sitecore.StringExtensions;
using System;
using System.Configuration;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace Sitecore.Support.Azure.Managers.Publishing
{
    public class PublishTargetRemoteManager
    {
        public void AddPublishTarget(PublishingTargetRemote publishTarget, AzureStorage storage)
        {
            AzureBlobManager.Current.UploadFromObject<PublishingTargetRemote>(storage, publishTarget, Sitecore.Azure.Configuration.Settings.PublishTargetsContainer, publishTarget.DatabaseName, null, true);
        }

        private XDocument GetConnectionStrings()
        {
            return XDocument.Load(this.GetConnectionStringsFilePath());
        }

        private string GetConnectionStringsFilePath()
        {
            ConnectionStringsSection expr_0F = ConfigurationManager.GetSection("connectionStrings") as ConnectionStringsSection;
            Assert.IsNotNull(expr_0F, "configSection");
            return FileUtil.MapPath(expr_0F.SectionInformation.ConfigSource.Replace('\\', '/'));
        }

        private AzureStorage GetStorageItem(string storageName)
        {
            object[] parameters = new object[]
            {
                AzureStorageItem.TemplateID,
                storageName
            };
            Item item = SitecoreDatabases.Master.SelectSingleItem("fast:/sitecore/system/Modules/Azure//*[@@templateid='{0}'  and @#Service Name#='{1}']".FormatWith(parameters));
            return new EntitiesFactory().Create(item, new Storages(null, item.Parent)) as AzureStorage;
        }

        private string GetTargetName(Uri uri)
        {
            return uri.Segments[uri.Segments.Length - 1];
        }

        private void InstallPublishTarget(PublishingTargetRemote publishTarget)
        {
            XDocument xDocument = null;
            bool flag = false;
            if (!Sitecore.Configuration.Settings.ConnectionStringExists(publishTarget.DatabaseName))
            {
                xDocument = this.GetConnectionStrings();
                object[] content = new object[]
                {
                    new XAttribute("name", publishTarget.DatabaseName),
                    new XAttribute("connectionString", publishTarget.ConnectionString)
                };
                XElement content2 = new XElement("add", content);
                XElement expr_73 = xDocument.Element("connectionStrings");
                Assert.IsNotNull(expr_73, "Connection strings section not found");
                expr_73.Add(content2);
                flag = true;
            }
            string text = FileUtil.MapPath("/App_Config/Include/publishTargets.config");
            XDocument xDocument2 = FileUtil.FileExists(text) ? XDocument.Load(text) : new XDocument(new object[]
            {
                new XElement("configuration", new XElement("sitecore", new XElement("databases")))
            });
            bool flag2 = false;
            object[] parameters = new object[]
            {
                publishTarget.DatabaseName
            };
            if (xDocument2.XPathSelectElement("configuration/sitecore/databases/database[@id={0}]".FormatWith(parameters)) == null)
            {
                XElement xElement = xDocument2.XPathSelectElement("configuration/sitecore/databases");
                if (xElement == null)
                {
                    xDocument2 = new XDocument(new object[]
                    {
                        new XElement("configuration", new XElement("sitecore", new XElement("databases")))
                    });
                    xElement = xDocument2.XPathSelectElement("configuration/sitecore/databases");
                }
                xElement.Add(publishTarget.DatabaseConfigNode);
                flag2 = true;
            }
            Database master = SitecoreDatabases.Master;
            if (master.GetItem("/sitecore/system/Publishing targets/" + publishTarget.DatabaseName) == null)
            {
                Item item = master.GetItem("/sitecore/system/Publishing targets").Add(publishTarget.DatabaseName, new TemplateID(new ID("{E130C748-C13B-40D5-B6C6-4B150DC3FAB3}")));
                using (new EditContext(item))
                {
                    item["Target database"] = publishTarget.DatabaseName;
                }
            }
            if (flag)
            {
                this.SaveConnectionString(xDocument);
            }
            if (flag2)
            {
                xDocument2.Save(text);
            }
        }

        private void SaveConnectionString(XDocument connectionStrings)
        {
            connectionStrings.Save(this.GetConnectionStringsFilePath());
        }

        public void Synchronize()
        {
            string configurationSettingValue = RoleEnvironment.GetConfigurationSettingValue("StorageName");
            AzureStorage storageItem = this.GetStorageItem(configurationSettingValue);
            object[] parameters = new object[]
            {
                storageItem.Protocol.StartsWith("https") ? "https" : "http",
                storageItem.AccountName,
                storageItem.PrimaryAccessKey,
                storageItem.Endpoint
            };
            string connectionString = "DefaultEndpointsProtocol={0};AccountName={1};AccountKey={2};EndpointSuffix={3};".FormatWith(parameters);
            foreach (IListBlobItem current in AzureBlobManager.Current.GetBlobStorageContainer(connectionString, Sitecore.Azure.Configuration.Settings.PublishTargetsContainer, null).ListBlobs(null, false, BlobListingDetails.None, null, null))
            {
                if (Factory.GetDatabase(this.GetTargetName(current.Uri), false) == null && current is CloudBlockBlob)
                {
                    PublishingTargetRemote publishingTargetRemote = AzureBlobManager.Current.DownloadToObject<PublishingTargetRemote>(current as CloudBlockBlob);
                    Assert.IsNotNull(publishingTargetRemote, "publishTarget");
                    if (SQLManager.CheckConnection(publishingTargetRemote.ConnectionString))
                    {
                        this.InstallPublishTarget(publishingTargetRemote);
                    }
                }
            }
        }
    }
}
