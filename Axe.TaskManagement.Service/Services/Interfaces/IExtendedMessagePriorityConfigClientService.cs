using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IExtendedMessagePriorityConfigClientService
{
    Task<GenericResponse<IEnumerable<ExtendedMessagePriorityConfigDto>>> GetByServiceExchangeProject(
        string serviceCode = null, string exchangeName = null, Guid? projectInstanceId = null,
        string accessToken = null);
}