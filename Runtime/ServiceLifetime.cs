namespace Validosik.Core.Ioc
{
    public enum ServiceLifetime
    {
        Singleton,  // one per state container
        Transient,  // new per resolve
        Shared      // shared accross states (alias to root)
    }
}
