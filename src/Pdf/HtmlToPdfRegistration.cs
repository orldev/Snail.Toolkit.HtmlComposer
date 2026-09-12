using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Snail.Toolkit.HtmlComposer.Fonts;

namespace Snail.Toolkit.HtmlComposer.Pdf;

/// <summary>Wires the PDF converter into an application.</summary>
public static class HtmlToPdfRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the PDF converter over the font library the application registered.</summary>
        /// <param name="options">What the converter is asked for; the defaults when omitted.</param>
        /// <remarks>
        /// Singleton, like the library it draws with: a converter keeps nothing between documents and one is
        /// shared between threads. The library comes from <see cref="FontsRegistration"/>, and a container
        /// without one says so at the first resolve.
        /// </remarks>
        public IServiceCollection AddHtmlToPdf(PdfOptions? options = null)
        {
            services.TryAddSingleton(provider => new HtmlToPdf(provider.GetRequiredService<FontLibrary>(), options));

            return services;
        }
    }
}
