using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class SequenceJobRepository : MongoBaseRepository<SequenceJob>, ISequenceJobRepository
    {
        public SequenceJobRepository(IMongoContext context) : base(context)
        {
        }

        public async Task<long> GetSequenceValue(string sequenceName)
        {
            var filter = Builders<SequenceJob>.Filter.Eq(s => s.SequenceName, sequenceName);
            var update = Builders<SequenceJob>.Update.Inc(s => s.SequenceValue, 1);

            var result = await DbSet.FindOneAndUpdateAsync(filter, update,
                new FindOneAndUpdateOptions<SequenceJob, SequenceJob>
                    { IsUpsert = true, ReturnDocument = ReturnDocument.After });

            return result?.SequenceValue ?? 0;
        }
    }
}
