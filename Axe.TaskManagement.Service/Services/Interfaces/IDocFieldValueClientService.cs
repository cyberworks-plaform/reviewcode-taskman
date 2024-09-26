using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IDocFieldValueClientService
    {
        Task<GenericResponse<IEnumerable<DocFieldValueDto>>> GetByDocTypeFieldInstanceIds(Guid docInstanceId, string docTypeFieldIntanceIds, string accessToken = null);

        Task<GenericResponse<int>> GetCountOfExpectedByDocInstanceId(Guid docInstanceId, string accessToken = null);

        Task<GenericResponse<IEnumerable<DocFieldValueDto>>> GetListByDocInstanceId(Guid docInstanceId, string accessToken);

        Task<GenericResponse<int>> DeleteByDocTypeFieldInstanceIds(Guid docInstanceId, string docTypeFieldIntanceIds, string accessToken = null);

        Task<GenericResponse<DocFieldValueDto>> GetByInstanceId(Guid instanceId, string accessToken = null);

        Task<GenericResponse<int>> UpdateMulti(List<DocFieldValueDto> docTypeFields, string accessToken = null);

        Task<GenericResponse<int>> UpdateMultiValue(DocFieldValueUpdateMultiValueEvent model, string accessToken = null);
    }
}