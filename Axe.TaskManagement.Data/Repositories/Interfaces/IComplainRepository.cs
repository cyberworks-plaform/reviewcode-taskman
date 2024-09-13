using System;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface IComplainRepository : IMongoBaseRepository<Complain>
    {
        Task<Complain> GetByJobCode(string code);
        Task<List<Complain>> GetByMultipleJobCode(List<string> listJobCode);
        Task<bool> CheckComplainProcessing(Guid docInstanceId);
        Task<PagedListExtension<Complain>> GetPagingExtensionAsync(FilterDefinition<Complain> filter, SortDefinition<Complain> sort = null, int index = 1, int size = 10);
    }
}
