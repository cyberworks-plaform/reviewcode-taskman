using System.Threading.Tasks;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Interfaces;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface ISequenceJobRepository : IMongoBaseRepository<SequenceJob>
    {
        Task<long> GetSequenceValue(string sequenceName);
    }
}