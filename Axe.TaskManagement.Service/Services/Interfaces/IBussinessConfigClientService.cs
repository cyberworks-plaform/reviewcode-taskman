using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IBussinessConfigClientService
    {
        Task<GenericResponse<List<BussinessConfigDto>>> GetByProjectInstanceId(Guid projectInstanceId, string codes, string accessToken = null);
    }
}