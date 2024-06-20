using System;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.DataAccess
{
    public interface IAxeTaskManagementUnitOfWork : IDisposable
    {
        int Commit();

        Task<int> CommitAsync();
    }
}
