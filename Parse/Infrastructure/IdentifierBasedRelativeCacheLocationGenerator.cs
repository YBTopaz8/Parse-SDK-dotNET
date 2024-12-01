using System;
using System.IO;
using Parse.Abstractions.Infrastructure;

namespace Parse.Infrastructure
{
    /// <summary>
    /// A configuration of the Parse SDK persistent storage location based on an identifier.
    /// </summary>
    public struct IdentifierBasedRelativeCacheLocationGenerator : IRelativeCacheLocationGenerator
    {
        internal static IdentifierBasedRelativeCacheLocationGenerator Fallback { get; } = new IdentifierBasedRelativeCacheLocationGenerator { IsFallback = true };

        /// <summary>
        /// Dictates whether or not this <see cref="IRelativeCacheLocationGenerator"/> instance should act as a fallback for when <see cref="ParseClient"/> has not yet been initialized but the storage path is needed.
        /// </summary>
        internal bool IsFallback { get; set; }

        /// <summary>
        /// The identifier that all Parse SDK cache files should be labelled with.
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// The corresponding relative path generated by this <see cref="IRelativeCacheLocationGenerator"/>.
        /// </summary>
        /// <remarks>This will cause a .cachefile file extension to be added to the cache file in order to prevent the creation of files with unwanted extensions due to the value of <see cref="Identifier"/> containing periods.</remarks>
        public string GetRelativeCacheFilePath(IServiceHub serviceHub)
        {
            FileInfo file;

            while ((file = serviceHub.CacheController.GetRelativeFile(GeneratePath())).Exists && IsFallback)
                ;

            return file.FullName;
        }

        /// <summary>
        /// Generates a path for use in the <see cref="GetRelativeCacheFilePath(IServiceHub)"/> method.
        /// </summary>
        /// <returns>A potential path to the cachefile</returns>
        string GeneratePath()
        {
            return Path.Combine(nameof(Parse), IsFallback ? "_fallback" : "_global", $"{(IsFallback ? new Random { }.Next().ToString() : Identifier)}.cachefile");
        }
    }
}
