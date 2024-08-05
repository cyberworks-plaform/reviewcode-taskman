using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface IComplainRepository : IMongoBaseRepository<Complain>
    {
        Task<Complain> GetByJobCode(string code);
        Task<PagedListExtension<Complain>> GetPagingExtensionAsync(FilterDefinition<Complain> filter, SortDefinition<Complain> sort = null, int index = 1, int size = 10);
    }
}
