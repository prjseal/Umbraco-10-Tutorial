using Microsoft.AspNetCore.Routing;
using Clean.Site.Controllers;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Extensions;

namespace Clean.Site.NotificationsHandlers
{
    internal class ServerVariablesParsingNotificationHandler : INotificationHandler<ServerVariablesParsingNotification>
    {
        private readonly LinkGenerator _linkGenerator;

        public ServerVariablesParsingNotificationHandler(LinkGenerator linkGenerator)
        {
            _linkGenerator = linkGenerator;
        }


        public void Handle(ServerVariablesParsingNotification notification)
        {
            notification.ServerVariables.Add("TwentyFourDays", new
            {
                PreviewApi = _linkGenerator.GetPathByAction(nameof(BlockPreviewApiController.PreviewMarkup), ControllerExtensions.GetControllerName<BlockPreviewApiController>())
            });
        }
    }
}
