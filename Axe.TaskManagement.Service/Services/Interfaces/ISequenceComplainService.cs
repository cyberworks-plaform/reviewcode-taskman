using System.Threading.Tasks;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface ISequenceComplainService : IMongoBaseService<SequenceComplain, SequenceComplainDto>
    {
        Task<GenericResponse<long>> GetSequenceValue(string sequenceName);
    }
}