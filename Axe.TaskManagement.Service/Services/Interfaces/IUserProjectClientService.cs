using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IUserProjectClientService
    {
        Task<GenericResponse<Guid>> GetPrimaryUserInstanceIdByProject(Guid projectInstanceId, string accessToken = null);

        Task<GenericResponse<IEnumerable<Guid>>> GetUserInstanceIdsByProject(Guid projectInstanceId, string accessToken = null);
    }
}