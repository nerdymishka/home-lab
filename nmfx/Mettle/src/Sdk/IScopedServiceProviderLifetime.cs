using System;

namespace Mettle.Sdk
{
    public interface IScopedServiceProviderLifetime : IDisposable
    {
        IServiceProvider Provider { get; }
    }
}