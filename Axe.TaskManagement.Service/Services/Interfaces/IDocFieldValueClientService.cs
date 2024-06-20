using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IDocFieldValueClientService
    {
        Task<GenericResponse<IEnumerable<DocFieldValueDto>>> GetByDocTypeFieldInstanceIds(Guid docInstanceId, string docTypeFieldIntanceIds, string accessToken = null);

        Task<GenericResponse<int>> GetCountOfExpectedByDocInstanceId(Guid docInstanceId, string accessToken = null);

        Task<GenericResponse<int>> DeleteByDocTypeFieldInstanceIds(Guid docInstanceId, string docTypeFieldIntanceIds, string accessToken = null);
    }
}