using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Models.Blocks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;
using Umbraco.Cms.Web.BackOffice.Controllers;

namespace Clean.Site.Controllers
{
    /// <summary>
    /// Represents the block preview api controller.
    /// </summary>
    public class BlockPreviewApiController : UmbracoAuthorizedJsonController
    {
        private readonly IPublishedRouter _publishedRouter;
        private readonly BlockEditorConverter _blockEditorConverter;
        private readonly ILogger<BlockPreviewApiController> _logger;
        private readonly IUmbracoContextAccessor _umbracoContextAccessor;
        private readonly IVariationContextAccessor _variationContextAccessor;
        private readonly IRazorViewEngine _razorViewEngine;
        private readonly ITempDataProvider _tempDataProvider;
        private readonly ITypeFinder _typeFinder;
        private readonly IPublishedValueFallback _publishedValueFallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlockPreviewApiController"/> class.
        /// </summary>
        /// <param name="publishedRouter">A <see cref="IPublishedRouter"/> instance.</param>
        /// <param name="blockEditorConverter">A <see cref="_blockEditorConverter"/> instance.</param>
        /// <param name="umbracoContextAccessor">A <see cref="IUmbracoContextAccessor"/> instance.</param>
        /// <param name="logger">A <see cref="ILogger{T}"/> instance.</param>
        /// <param name="variationContextAccessor">A <see cref="IVariationContextAccessor"/> instance.</param>
        public BlockPreviewApiController(IPublishedRouter publishedRouter, BlockEditorConverter blockEditorConverter, IUmbracoContextAccessor umbracoContextAccessor, ILogger<BlockPreviewApiController> logger, IVariationContextAccessor variationContextAccessor, IRazorViewEngine razorViewEngine, ITempDataProvider tempDataProvider, ITypeFinder typeFinder, IPublishedValueFallback publishedValueFallback)
        {
            _publishedRouter = publishedRouter;
            _blockEditorConverter = blockEditorConverter;
            _umbracoContextAccessor = umbracoContextAccessor;
            _logger = logger;
            _variationContextAccessor = variationContextAccessor;
            _razorViewEngine = razorViewEngine;
            _tempDataProvider = tempDataProvider;
            _typeFinder = typeFinder;
            _publishedValueFallback = publishedValueFallback;
        }

        /// <summary>
        /// Renders a preview for a block using the associated razor view.
        /// </summary>
        /// <param name="data">The json data of the block.</param>
        /// <param name="pageId">The current page id.</param>
        /// <param name="culture">The culture</param>
        /// <returns>The markup to render in the preview.</returns>
        [HttpPost]
        public async Task<IActionResult> PreviewMarkup([FromBody] BlockItemData data, [FromQuery] int pageId = 0, [FromQuery] string culture = "")
        {
            string markup;

            try
            {
                IPublishedContent page = null;

                // If the page is new, then the ID will be zero
                if (pageId > 0)
                {
                    page = this.GetPublishedContentForPage(pageId);
                }

                if (page == null)
                {
                    markup = "The page is not saved yet, so we can't create a preview. Save the page first.";
                    return Ok(markup);
                }

                await this.SetupPublishedRequest(culture, page);
                
                markup = await this.GetMarkupForBlock(data);
            }
            catch (Exception ex)
            {
                markup = "Something went wrong rendering a preview.";
                this._logger.LogError(ex, "Error rendering preview for a block");
            }

            return Ok(this.CleanUpMarkup(markup));
        }

        private async Task<string> GetMarkupForBlock(BlockItemData blockData)
        {
           
            // convert the json data to a IPublishedElement (using the built-in conversion)
            var element = this._blockEditorConverter.ConvertToElement(blockData, PropertyCacheLevel.None, true);

            // get the models builder type based on content type alias
            var blockType = _typeFinder.FindClassesWithAttribute<PublishedModelAttribute>().FirstOrDefault(x =>
                x.GetCustomAttribute<PublishedModelAttribute>(false).ContentTypeAlias == element.ContentType.Alias);

            // create instance of the models builder type based from the element
            var blockInstance = Activator.CreateInstance(blockType, element, _publishedValueFallback);

            // get a generic block list item type based on the models builder type
            var blockListItemType = typeof(BlockListItem<>).MakeGenericType(blockType);

            // create instance of the block list item
            // if you want to use settings this will need to be changed.
            var blockListItem = (BlockListItem)Activator.CreateInstance(blockListItemType, blockData.Udi, blockInstance, null, null);

            // render the partial view for the block.
            var partialName = $"/Views/Partials/blocklist/Components/{element.ContentType.Alias}.cshtml";

            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary());
            viewData.Model = blockListItem;
            
            var actionContext = new ActionContext(this.HttpContext, new RouteData(), new ActionDescriptor());

            await using var sw = new StringWriter();
            var viewResult = _razorViewEngine.GetView(partialName, partialName, false);
            
            if (viewResult?.View != null)
            {
                var viewContext = new ViewContext(actionContext, viewResult.View, viewData, new TempDataDictionary(actionContext.HttpContext, _tempDataProvider), sw, new HtmlHelperOptions());
                await viewResult.View.RenderAsync(viewContext);
            }

            return sw.ToString();
        }

        private async Task SetupPublishedRequest(string culture, IPublishedContent page)
        {
            // set the published request for the page we are editing in the back office
            if (!_umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext context))
            {
                return;
            }

            // set the published request
            var requestBuilder = await _publishedRouter.CreateRequestAsync(new Uri(Request.GetDisplayUrl()));
            requestBuilder.SetPublishedContent(page);
            context.PublishedRequest = requestBuilder.Build();

            if (page.Cultures == null)
            {
                return;
            }

            // if in a culture variant setup also set the correct language.
            var currentCulture = string.IsNullOrWhiteSpace(culture) ? page.GetCultureFromDomains() : culture;

            if (currentCulture == null || !page.Cultures.ContainsKey(currentCulture))
            {
                return;
            }

            var cultureInfo = new CultureInfo(page.Cultures[currentCulture].Culture);
          
            System.Threading.Thread.CurrentThread.CurrentCulture = cultureInfo;
            System.Threading.Thread.CurrentThread.CurrentUICulture = cultureInfo;
            _variationContextAccessor.VariationContext = new VariationContext(cultureInfo.Name);
        }

        private IPublishedContent GetPublishedContentForPage(int pageId)
        {
            if (!_umbracoContextAccessor.TryGetUmbracoContext(out IUmbracoContext context))
            {
                return null;
            }

            // Get page from published cache.
            var page = context.Content.GetById(pageId);

            if (page == null)
            {
                // If unpublished, then get it from preview
                page = context.Content.GetById(true, pageId);
            }

            return page;
        }

        private string CleanUpMarkup(string markup)
        {
            if (string.IsNullOrWhiteSpace(markup))
            {
                return markup;
            }

            var content = new HtmlDocument();
            content.LoadHtml(markup);

            // make sure links are not clickable in the back office, because this will prevent editing
            var links = content.DocumentNode.SelectNodes("//a");

            if (links != null)
            {
                foreach (var link in links)
                {
                    link.SetAttributeValue("href", "javascript:;");
                }
            }
            
            return content.DocumentNode.OuterHtml;
        }
    }
}
