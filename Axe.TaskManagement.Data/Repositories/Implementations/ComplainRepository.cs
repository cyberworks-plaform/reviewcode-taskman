using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Model.Enums;
using Axe.Utility.Definitions;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
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
            return await DbSet.Find(Builders<Complain>.Filter.Eq(x => x.JobCode, code))
                .Sort(Builders<Complain>.Sort.Descending(nameof(Complain.CreatedDate))).SingleOrDefaultAsync();
        }

        public Task<List<Complain>> GetByMultipleJobCode(List<string> listJobCode)
        {
            var filter = Builders<Complain>.Filter.In(x => x.JobCode, listJobCode);
            var listComplain = DbSet.Find(filter);
            return listComplain.ToListAsync();
        }

        public async Task<bool> CheckComplainProcessing(Guid docInstanceId)
        {
            var filter = Builders<Complain>.Filter.Eq(x => x.DocInstanceId, docInstanceId) &
                         Builders<Complain>.Filter.Eq(x => x.Status, (short)EnumComplain.Status.Processing);
            var entity = await DbSet.Find(filter).SingleOrDefaultAsync();
            return entity != null;
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
                : await DbSet.EstimatedDocumentCountAsync();

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
