using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.Utility.Definitions;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class ComplainRepository : MongoBaseRepository<Complain>, IComplainRepository
    {
        public ComplainRepository(IMongoContext context) : base(context)
        {
        }

        public async Task<Complain> GetByJobCode(string code)
        {
            var filter = Builders<Complain>.Filter.Eq(x => x.JobCode, code);
            var record = DbSet.Find(filter);
            var records = await record.ToListAsync();
            if (records != null && records.Count > 0)
            {
                return records.OrderByDescending(x => x.CreatedDate).First();
            }
            return null;
        }

        public async Task<PagedListExtension<Complain>> GetPagingExtensionAsync(FilterDefinition<Complain> filter, SortDefinition<Complain> sort = null, int index = 1, int size = 10)
        {
            var isNullFilter = false;
            IFindFluent<Complain, Complain> data;
            if (filter == null)
            {
                filter = Builders<Complain>.Filter.Empty;
                isNullFilter = true;
            }

            if (index == -1)
            {
                data = DbSet.Find(filter);
            }
            else
            {
                data = sort == null ?
                DbSet.Find(filter).Skip((index - 1) * size).Limit(size)
                : DbSet.Find(filter).Sort(sort).Skip((index - 1) * size).Limit(size);
            }

            var totalfilter = await DbSet.CountDocumentsAsync(filter);
            long totalCorrect = await DbSet.CountDocumentsAsync(filter & Builders<Complain>.Filter.Eq(x => x.ActionCode, nameof(ActionCodeConstants.DataEntry)) & Builders<Complain>.Filter.Eq(x => x.RightStatus, (int)EnumJob.RightStatus.Correct));
            var totalComplete = await DbSet.CountDocumentsAsync(filter & Builders<Complain>.Filter.Eq(x => x.Status, (int)EnumJob.Status.Complete));
            
            var total = isNullFilter ?
                totalfilter
                : await DbSet.CountDocumentsAsync(Builders<Complain>.Filter.Empty);

            return new PagedListExtension<Complain>
            {
                Data = data.ToList(),
                PageIndex = index,
                PageSize = size,
                TotalCount = total,
                TotalCorrect = totalCorrect,
                TotalComplete = totalComplete,
                TotalFilter = (int)totalfilter,
                TotalPages = (int)Math.Ceiling((decimal)totalfilter / size)
            };
        }
    }
}
