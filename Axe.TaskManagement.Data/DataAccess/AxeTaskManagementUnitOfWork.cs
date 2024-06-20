using Ce.Common.Lib.Abstractions;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.DataAccess
{
    public class AxeTaskManagementUnitOfWork : Disposable, IAxeTaskManagementUnitOfWork
    {
        private readonly AxeTaskManagementDbContext _dataContext;

        public AxeTaskManagementUnitOfWork(AxeTaskManagementDbContext dbContext)
        {
            _dataContext = dbContext;
        }

        public int Commit()
        {
            return _dataContext.SaveChanges();
        }

        public Task<int> CommitAsync()
        {
            return _dataContext.SaveChangesAsync();
        }

        protected override void DisposeCore()
        {
            if (_dataContext != null)
                _dataContext.Dispose();
        }
    }
}
