using System.Threading.Tasks;

namespace Validosik.Core.Ioc.Interfaces
{
    public interface IInitializable
    {
        // Called after instance creation and dependencies wired
        Task InitializeAsync(IServiceContainer scope);
    }
}
