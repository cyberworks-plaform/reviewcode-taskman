using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IDocTypeFieldClientService
{
    Task<GenericResponse<List<DocTypeFieldDto>>> GetByProjectAndDigitizedTemplateInstanceId(Guid projectInstanceId, Guid digitizedTemplateInstanceId, string accessToken);
}