using System;
using System.Collections.Generic;

namespace Validosik.Core.Ioc
{
    internal class DependenciesInfo
    {
        private readonly List<ServiceInfo> _serviceInfos = new List<ServiceInfo>();
        
        
    }

    internal struct ServiceInfo
    {
        private readonly string _serviceGuid;
        private readonly string[] _firstLineDependencies;
        private int _depth;

        internal ServiceInfo(string serviceGuid, string[] firstLineDependencies)
        {
            _serviceGuid = serviceGuid;
            _firstLineDependencies = firstLineDependencies;
            
            _depth = 0;
        }

        internal void UpdateMaxDepth(int dependentDepth)
        {
            _depth = Math.Max(dependentDepth + 1, _depth);
        }
    }
}