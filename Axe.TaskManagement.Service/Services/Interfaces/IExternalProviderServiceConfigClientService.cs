using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IExternalProviderServiceConfigClientService
    {
        Task<GenericResponse<ExternalProviderServiceConfigDto>> GetPrimaryConfigForActionCode(string actionCode, string digitizedTemplateCode = null, string accessToken = null);
    }
}
