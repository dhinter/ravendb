﻿using System.Net;
using System.Threading.Tasks;
using Raven.Server.Commercial;
using Raven.Server.Json;
using Raven.Server.Routing;
using Sparrow.Json;

namespace Raven.Server.Web.Studio
{
    public class LicenseHandler : RequestHandler
    {
        [RavenAction("/license/status", "GET", AuthorizationStatus.ValidUser)]
        public Task Status()
        {
            using (var context = JsonOperationContext.ShortTermSingleUse())
            using (var writer = new BlittableJsonTextWriter(context, ResponseBodyStream()))
            {
                context.Write(writer, ServerStore.LicenseManager.GetLicenseStatus().ToJson());
            }

            return Task.CompletedTask;
        }
 
        [RavenAction("/admin/license/activate", "POST", AuthorizationStatus.ClusterAdmin)]
        public async Task Activate()
        {
            License license;

            using (var context = JsonOperationContext.ShortTermSingleUse())
            {
                var json = context.Read(RequestBodyStream(), "license activation");
                license = JsonDeserializationServer.License(json);
            }

            var licenseLimit = await ServerStore.LicenseManager.Activate(license, skipLeaseLicense: false);
            if (licenseLimit != null)
            {
                SetLicenseLimitResponse(licenseLimit);
                return;
            }

            NoContentStatus();
        }

        [RavenAction("/admin/license/deactivate", "POST", AuthorizationStatus.ClusterAdmin)]
        public async Task Deactivate()
        {
            if (ServerStore.Configuration.Licensing.CanDeactivate == false)
            {
                HttpContext.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            await ServerStore.LicenseManager.DeactivateLicense();

            NoContentStatus();
        }
    }
}
