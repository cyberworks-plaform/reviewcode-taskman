using Axe.TaskManagement.Model.Entities;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.Utility.EntityExtensions;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface IComplainRepository : IMongoBaseRepository<Complain>
    {
        Task<Complain> GetByJobCode(string code);
        Task<Complain> GetByInstanceId(string instanceId);
        Task<PagedListExtension<Complain>> GetPagingExtensionAsync(FilterDefinition<Complain> filter, SortDefinition<Complain> sort = null, int index = 1, int size = 10);
    }
}
