using System;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IProjectClientService
    {
        Task<GenericResponse<ProjectDto>> GetByInstanceIdAsync(Guid instanceId, string accessToken = null);
    }
}