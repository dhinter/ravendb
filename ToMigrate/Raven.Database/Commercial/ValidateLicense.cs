//-----------------------------------------------------------------------
// <copyright file="ValidateLicense.cs" company="Hibernating Rhinos LTD">
//     Copyright (c) Hibernating Rhinos LTD. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using Raven.Abstractions;
using Raven.Abstractions.Data;
using Raven.Abstractions.Logging;
using Raven.Database.Config;
using Raven.Database.Extensions;
using Raven.Database.Impl.Clustering;
using Rhino.Licensing;
using Rhino.Licensing.Discovery;

namespace Raven.Database.Commercial
{
    internal class ValidateLicense : IDisposable
    {
        public static LicensingStatus CurrentLicense { get; set; }
        public static Dictionary<string, string> LicenseAttributes { get; set; }
        public static event Action<LicensingStatus>  CurrentLicenseChanged;
        private AbstractLicenseValidator licenseValidator;
        private readonly ILog logger = LogManager.GetCurrentClassLogger();
        private Timer timer;

        private static readonly Dictionary<string, string> AlwaysOnAttributes = new Dictionary<string, string>
        {
            {"periodicBackup", "false"},
            {"encryption", "false"},
            {"fips", "false"},
            {"globalConfiguration", "false"},
            {"compression", "false"},
            {"quotas","false"},
            {"ravenfs", "false"},
            {"counters", "false"},
            {"timeSeries", "false"},

            {"authorization","true"},
            {"documentExpiration","true"},
            {"replication","true"},
            {"versioning","true"},
            {"cluster","false"},
            {"monitoring","false"}
        };

        static ValidateLicense()
        {
            CurrentLicense = new LicensingStatus
            {
                Status = "AGPL - Open Source",
                Error = false,
                Message = "No license file was found.\r\n" +
                          "The AGPL license restrictions apply, only Open Source / Development work is permitted.",
                Attributes = new Dictionary<string, string>(AlwaysOnAttributes, StringComparer.OrdinalIgnoreCase)
            };
        }

        public void Execute(RavenConfiguration config)
        {
            //We defer GettingNewLeaseSubscription the first time we run, we will run again with 1 minute so not to delay startup.
            timer = new Timer(state => ExecuteInternal(config), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(15));

            ExecuteInternal(config,true);
        }

        public void ForceExecute(RavenConfiguration config)
        {
            ExecuteInternal(config);
        }

        private void ExecuteInternal(RavenConfiguration config, bool firstTime = false)
        {
            var licensePath = GetLicensePath(config);
            var licenseText = GetLicenseText(config);

            if (TryLoadLicense(config) == false)
                return;

            string errorMessage = string.Empty;
            try
            {

                try
                {
                    licenseValidator.AssertValidLicense(() =>
                    {
                        string value;

                        errorMessage = AssertLicenseAttributes(licenseValidator.LicenseAttributes, licenseValidator.LicenseType);
                        if (licenseValidator.LicenseAttributes.TryGetValue("OEM", out value) &&
                            "true".Equals(value, StringComparison.OrdinalIgnoreCase))
                        {
                            licenseValidator.MultipleLicenseUsageBehavior = AbstractLicenseValidator.MultipleLicenseUsage.AllowSameLicense;
                        }
                        string allowExternalBundles;
                        if (licenseValidator.LicenseAttributes.TryGetValue("allowExternalBundles", out allowExternalBundles) &&
                            bool.Parse(allowExternalBundles) == false)
                        {
                            var directoryCatalogs = config.Catalog.Catalogs.OfType<DirectoryCatalog>().ToArray();
                            foreach (var catalog in directoryCatalogs)
                            {
                                config.Catalog.Catalogs.Remove(catalog);
                            }
                        }
                    }, config.Core.TurnOffDiscoveryClient, firstTime);
                }
                catch (LicenseExpiredException ex)
                {
                    errorMessage = ex.Message;
                }

                var attributes = new Dictionary<string, string>(AlwaysOnAttributes, StringComparer.OrdinalIgnoreCase);
                foreach (var licenseAttribute in licenseValidator.LicenseAttributes)
                {
                    attributes[licenseAttribute.Key] = licenseAttribute.Value;
                }
                attributes["UserId"] = licenseValidator.UserId.ToString();
                var message = "Valid license at " + licensePath;
                var status = "Commercial";
                if (licenseValidator.LicenseType != LicenseType.Standard)
                    status += " - " + licenseValidator.LicenseType;

                if (licenseValidator.IsOemLicense() && licenseValidator.ExpirationDate < SystemTime.UtcNow)
                {
                    message = string.Format("Expired ({0}) OEM/ISV license at {1}", licenseValidator.ExpirationDate.ToShortDateString(), licensePath);
                    status += " (Expired)";
                }

                CurrentLicense = new LicensingStatus
                {
                    Status = status,
                    Error = !String.IsNullOrEmpty(errorMessage),
                    Message = String.IsNullOrEmpty(errorMessage) ? message : errorMessage,
                    Attributes = attributes
                };
                if (CurrentLicenseChanged != null)
                    CurrentLicenseChanged(CurrentLicense);
            }
            catch (Exception e)
            {
                logger.ErrorException("Could not validate license at " + licensePath + ", " + licenseText, e);

                try
                {
                    var xmlDocument = new XmlDocument();
                    xmlDocument.Load(licensePath);
                    var ns = new XmlNamespaceManager(xmlDocument.NameTable);
                    ns.AddNamespace("sig", "http://www.w3.org/2000/09/xmldsig#");
                    var sig = xmlDocument.SelectSingleNode("/license/sig:Signature", ns);
                    if (sig != null)
                    {
                        sig.RemoveAll();
                    }
                    licenseText = xmlDocument.InnerXml;
                }
                catch (Exception)
                {
                    // couldn't remove the signature, maybe not XML?
                }

                CurrentLicense = new LicensingStatus
                {
                    Status = "AGPL - Open Source",
                    Error = true,
                    Details = "License Path: " + licensePath + Environment.NewLine + ", License Text: " + licenseText + Environment.NewLine + ", Exception: " + e,
                    Message = "Could not validate license: " + e.Message,
                    Attributes = new Dictionary<string, string>(AlwaysOnAttributes, StringComparer.OrdinalIgnoreCase)
                };
            }
        }

        private bool TryLoadLicense(RavenConfiguration config)
        {
            string publicKey;
            using (
                var stream = typeof(ValidateLicense).Assembly.GetManifestResourceStream("Raven.Database.Commercial.RavenDB.public"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Could not find public key for the license");
                publicKey = new StreamReader(stream).ReadToEnd();
            }

            var value = GetLicenseText(config);

            var fullPath = GetLicensePath(config).ToFullPath();

            if (IsSameLicense(value, fullPath))
                return licenseValidator != null;

            if (licenseValidator != null)
                licenseValidator.Dispose();

            if (string.IsNullOrEmpty(value) == false)
            {
                licenseValidator = new StringLicenseValidator(publicKey, value);
            }
            else if (File.Exists(fullPath))
            {
                licenseValidator = new LicenseValidator(publicKey, fullPath);
            }
            else
            {
                CurrentLicense = new LicensingStatus
                {
                    Status = "AGPL - Open Source",
                    Error = false,
                    Message = "No license file was found at " + fullPath +
                              "\r\nThe AGPL license restrictions apply, only Open Source / Development work is permitted."
                };
                return false;
            }

            licenseValidator.DisableFloatingLicenses = true;
            licenseValidator.SubscriptionEndpoint = "http://licensing.ravendb.net/Subscriptions.svc";
            licenseValidator.LicenseInvalidated += OnLicenseInvalidated;
            licenseValidator.MultipleLicensesWereDiscovered += OnMultipleLicensesWereDiscovered;

            return true;
        }

        private bool IsSameLicense(string value, string fullPath)
        {
            var stringLicenseValidator = licenseValidator as StringLicenseValidator;
            if (stringLicenseValidator != null)
                return stringLicenseValidator.SameLicense(value);

            var validator = licenseValidator as LicenseValidator;
            if (validator != null)
                return validator.SameFile(fullPath);

            return false;
        }

        private string AssertLicenseAttributes(IDictionary<string, string> licenseAttributes, LicenseType licenseType)
        {
            string version;
            var errorMessage = string.Empty;

            licenseAttributes.TryGetValue("version", out version); // note that a 1.0 license might not have version

            if (version != "3.0")
            {
                if (licenseType != LicenseType.Subscription)
                {
                    throw new LicenseExpiredException("This is not a license for RavenDB 3.0");
                }
            }

            string maxRam;
            if (licenseAttributes.TryGetValue("maxRamUtilization", out maxRam))
            {
                if (string.Equals(maxRam, "unlimited", StringComparison.OrdinalIgnoreCase) == false)
                {
                    MemoryStatistics.MemoryLimit = (int)(long.Parse(maxRam) / 1024 / 1024);
                }
            }

            string maxParallel;
            if (licenseAttributes.TryGetValue("maxParallelism", out maxParallel))
            {
                if (string.Equals(maxParallel, "unlimited", StringComparison.OrdinalIgnoreCase) == false)
                {
                    MemoryStatistics.MaxParallelism = Math.Max(2, (int.Parse(maxParallel) * 2));
                }
            }
            var clasterInspector = new ClusterInspecter();

            string claster;
            if (licenseAttributes.TryGetValue("allowWindowsClustering", out claster))
            {
                if (bool.Parse(claster) == false)
                {
                    if (clasterInspector.IsRavenRunningAsClusterGenericService())
                        throw new LicenseExpiredException("Your license does not allow clustering, but RavenDB is running in clustered mode");
                }
            }
            else
            {
                if (clasterInspector.IsRavenRunningAsClusterGenericService())
                    throw new LicenseExpiredException("Your license does not allow clustering, but RavenDB is running in clustered mode");
            }

            return errorMessage;
        }

        private string GetLicenseText(RavenConfiguration config)
        {
            var value = config.Licensing.License;

            if (string.IsNullOrEmpty(value) == false)
                return value;

            var fullPath = GetLicensePath(config).ToFullPath();

            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath);

            return string.Empty;
        }

        private static string GetLicensePath(RavenConfiguration config)
        {
            var value = config.Licensing.License;

            if (string.IsNullOrEmpty(value) == false)
                return "configuration";

            value = config.Licensing.LicensePath;

            if (string.IsNullOrEmpty(value) == false)
                return value;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "license.xml");
        }

        private void OnMultipleLicensesWereDiscovered(object sender, DiscoveryHost.ClientDiscoveredEventArgs clientDiscoveredEventArgs)
        {
            logger.Error("A duplicate license was found at {0} for user {1}. User Id: {2}. Both licenses were disabled!",
                clientDiscoveredEventArgs.MachineName,
                clientDiscoveredEventArgs.UserName,
                clientDiscoveredEventArgs.UserId);

            CurrentLicense = new LicensingStatus
            {
                Status = "AGPL - Open Source",
                Error = true,
                Message =
                    string.Format("A duplicate license was found at {0} for user {1}. User Id: {2}.", clientDiscoveredEventArgs.MachineName,
                                  clientDiscoveredEventArgs.UserName,
                                  clientDiscoveredEventArgs.UserId)
            };
        }

        private void OnLicenseInvalidated(InvalidationType invalidationType)
        {
            logger.Error("The license have expired and can no longer be used");
            CurrentLicense = new LicensingStatus
            {
                Status = "AGPL - Open Source",
                Error = true,
                Message = "License expired"
            };
        }

        public void Dispose()
        {
            if (timer != null)
                timer.Dispose();
            CurrentLicenseChanged = null;
        }
    }
}
