using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IBatchClientService
{
    Task<GenericResponse<BatchDto>> CreateBatch(BatchDto model, string accessToken = null);
}