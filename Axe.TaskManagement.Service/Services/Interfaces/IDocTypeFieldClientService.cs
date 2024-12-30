using Ce.Constant.Lib.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Axe.TaskManagement.Service.Dtos;
using Axe.Utility.EntityExtensions;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IDocTypeFieldClientService
{
    List<DocItem> ConvertToDocItem(List<DocTypeFieldDto> listDocTypeField);
    Task<GenericResponse<List<DocTypeFieldDto>>> GetByProjectAndDigitizedTemplateInstanceId(Guid projectInstanceId, Guid digitizedTemplateInstanceId, string accessToken);
}