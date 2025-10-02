using System;
using System.Collections.Generic;

namespace Validosik.Core.Ioc.Generated
{
    /// <summary>
    /// Injection point for generated registries compiled in the game project.
    /// Project codegen assigns Provider at startup; runtime reads via GetAll().
    /// </summary>
    public static class ContainerRegistrySource
    {
        // ReSharper disable once UnassignedField.Global
        /// <summary>Assigned by generated index in the game project.</summary>
        public static Func<IEnumerable<IGeneratedContainerRegistry>> Provider;

        /// <summary>Returns registries provided by the project; empty if none.</summary>
        public static IEnumerable<IGeneratedContainerRegistry> GetAll()
            => Provider != null ? Provider() : Array.Empty<IGeneratedContainerRegistry>();
    }
}