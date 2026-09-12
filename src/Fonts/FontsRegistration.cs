using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Snail.Toolkit.HtmlComposer.Fonts;

/// <summary>Wires the font library into an application.</summary>
public static class FontsRegistration
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers one font library, loaded once, for every converter to set documents in.</summary>
        /// <param name="load">Loads the faces: from a folder, a file, a stream or an embedded resource.</param>
        /// <remarks>
        /// Singleton, because a library is only read once loaded and every conversion has to measure with the
        /// same faces; the container disposes it with itself. Registered once: a second registration leaves the
        /// first in place rather than loading the faces again.
        /// </remarks>
        public IServiceCollection AddFonts(Action<FontLibrary> load)
        {
            ArgumentNullException.ThrowIfNull(load);

            services.TryAddSingleton(_ =>
            {
                var fonts = new FontLibrary();
                load(fonts);

                return fonts;
            });

            return services;
        }
    }
}
