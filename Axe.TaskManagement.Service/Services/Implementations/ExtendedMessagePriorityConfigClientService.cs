using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class ExtendedMessagePriorityConfigClientService : IExtendedMessagePriorityConfigClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;
        public ExtendedMessagePriorityConfigClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/extended-message-priority-config";
        }

        public async Task<GenericResponse<IEnumerable<ExtendedMessagePriorityConfigDto>>> GetByServiceExchangeProject(
            string serviceCode, string exchangeName, Guid? projectInstanceId,
            string accessToken)
        {
            GenericResponse<IEnumerable<ExtendedMessagePriorityConfigDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "get-by-service-exchange-project";
                var requestParam = new Dictionary<string, string>
                {
                    {"serviceCode", serviceCode},
                    {"exchangeName", exchangeName},
                    {"projectInstanceId", projectInstanceId?.ToString()}
                };
                response = await client.GetAsync<GenericResponse<IEnumerable<ExtendedMessagePriorityConfigDto>>>(
                    _serviceUri, apiEndpoint, requestParam, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<IEnumerable<ExtendedMessagePriorityConfigDto>>.ResultWithError(
                    (int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Log.Error(ex, ex.Message);
            }

            return response;
        }
    }
}
