using System;
using System.Net.Mail;
using Aggregator.Core.Configuration;
using Aggregator.Core.Context;
using Aggregator.Core.Interfaces;
using Aggregator.Core.Monitoring;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;
using Microsoft.TeamFoundation.Framework.Server;

#if TFS2015u1
using IVssRequestContext = Microsoft.TeamFoundation.Framework.Server.IVssRequestContext;
#else
using IVssRequestContext = Microsoft.TeamFoundation.Framework.Server.TeamFoundationRequestContext;
#endif

namespace Aggregator.Core.Facade
{
    public class ScriptLibrary : IScriptLibrary
    {

        private readonly ILogEvents logger;
        private readonly IRequestContext requestContext;
        private readonly ConnectionInfo connectionInfo;
        private readonly Mailer mailer;

        public ScriptLibrary(IRuntimeContext context)
        {
            this.connectionInfo = context.GetConnectionInfo();
            this.requestContext = context.RequestContext;
            this.logger = context.Logger;
            this.mailer = new Mailer(context.RequestContext.VssContext);
        }

        private class Mailer
        {
#if TFS2017
            readonly string NotificationRootPath = FrameworkServerConstants.NotificationRootPath;
#else
            // HACK is it completely useless?
            readonly string NotificationRootPath = "/Service/Integration/Settings";
#endif
#pragma warning disable SA1306 // Field names must begin with lower-case letter
            private readonly bool Enabled;
            private readonly string SmtpServer;
            private readonly int SmtpPort;
            private readonly bool EnableSsl;
            private readonly MailAddress FromAddress;
#pragma warning restore SA1306 // Field names must begin with lower-case letter

            internal Mailer(IVssRequestContext requestContext)
            {
                this.Enabled = false;
                try
                {
                    TeamFoundationRegistryService service = requestContext.GetService<TeamFoundationRegistryService>();
                    Microsoft.TeamFoundation.Framework.Server.RegistryEntryCollection registryEntryCollection = service.ReadEntriesFallThru(requestContext, this.NotificationRootPath + "/*");
                    if (registryEntryCollection["EmailEnabled"].GetValue<bool>(true))
                    {
                        this.SmtpServer = registryEntryCollection["SmtpServer"].GetValue(string.Empty);
                        this.SmtpPort = registryEntryCollection["SmtpPort"].GetValue<int>(-1);
                        this.EnableSsl = registryEntryCollection["SmtpEnableSsl"].GetValue<bool>(false);
                        this.FromAddress = null;

                        string value = registryEntryCollection["EmailNotificationFromAddress"].GetValue(string.Empty);
                        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(this.SmtpServer))
                        {
                            this.FromAddress = new MailAddress(value);
                            this.Enabled = true;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SendMail failed: {ex.Message}");
                }
            }

            internal void Send(string to, string subject, string body)
            {
                if (this.Enabled)
                {
                    using (SmtpClient client = new SmtpClient())
                    {
                        client.Host = this.SmtpServer;
                        client.Port = this.SmtpPort;
                        client.EnableSsl = this.EnableSsl;

                        MailMessage message = new MailMessage();
                        message.From = this.FromAddress;
                        message.To.Add(to);
                        message.Subject = subject;
                        message.Body = body;

                        client.Send(message);
                    }
                }
            }
        }

        public void SendMail(string to, string subject, string body)
        {
            try
            {
                this.mailer.Send(to, subject, body);
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendMail failed: {ex.Message}");
            }
        }

        // Get Email Address from TFS Account or Display Name
        // source: https://paulselles.wordpress.com/2014/03/24/tfs-api-tfs-user-email-address-lookup-and-reverse-lookup/
        public string GetEmailAddress(string userName, string defaultValue)
        {
            using (var teamProjectCollection = this.connectionInfo.Token.GetCollection(this.connectionInfo.ProjectCollectionUri))
            {
                var identityManagementService = teamProjectCollection.GetService<IIdentityManagementService>();

                TeamFoundationIdentity identity = identityManagementService.ReadIdentity(
                    IdentitySearchFactor.AccountName,
                    userName,
                    MembershipQuery.None,
                    ReadIdentityOptions.ExtendedProperties);

                // if not found try again using DisplayName
                identity = identity ?? identityManagementService.ReadIdentity(
                    IdentitySearchFactor.DisplayName,
                    userName,
                    MembershipQuery.None,
                    ReadIdentityOptions.ExtendedProperties);

                if (identity == null)
                {
                    return defaultValue;
                }

                // pick first non-null value
                string mailAddress = identity.GetAttribute("Mail", null);
                mailAddress = string.IsNullOrWhiteSpace(mailAddress) ?
                    identity.GetAttribute("ConfirmedNotificationAddress", defaultValue)
                    : mailAddress;

                return mailAddress;
            }
        }
    }
}
