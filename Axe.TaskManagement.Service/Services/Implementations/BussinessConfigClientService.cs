using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class BussinessConfigClientService : IBussinessConfigClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public BussinessConfigClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/bussiness-config";
        }

        public async Task<GenericResponse<List<BussinessConfigDto>>> GetByProjectInstanceId(Guid projectInstanceId, string codes, string accessToken = null)
        {
            GenericResponse<List<BussinessConfigDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "get-by-project-instance-id";
                var requestParam = new Dictionary<string, string>
                {
                    { "projectInstanceId",  projectInstanceId.ToString()},
                    { "codes",  codes}
                };
                response = await client.GetAsync<GenericResponse<List<BussinessConfigDto>>>(_serviceUri, apiEndpoint, requestParam, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<BussinessConfigDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
