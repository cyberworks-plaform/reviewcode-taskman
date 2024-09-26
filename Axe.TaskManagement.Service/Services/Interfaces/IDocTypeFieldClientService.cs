using Ce.Constant.Lib.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Axe.TaskManagement.Service.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IDocTypeFieldClientService
{
    Task<GenericResponse<List<DocTypeFieldDto>>> GetByProjectAndDigitizedTemplateInstanceId(Guid projectInstanceId, Guid digitizedTemplateInstanceId, string accessToken);
}