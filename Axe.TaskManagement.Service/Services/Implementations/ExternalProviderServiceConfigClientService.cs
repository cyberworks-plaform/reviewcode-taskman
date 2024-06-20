using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class ExternalProviderServiceConfigClientService : IExternalProviderServiceConfigClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public ExternalProviderServiceConfigClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/external-provider-service-config";
        }

        public async Task<GenericResponse<ExternalProviderServiceConfigDto>> GetPrimaryConfigForActionCode(string actionCode, string digitizedTemplateCode = null, string accessToken = null)
        {
            GenericResponse<ExternalProviderServiceConfigDto> response;
            try
            {
                var client = _clientFatory.Create();
                var requestParams = new Dictionary<string, string>
                {
                    { "actionCode", actionCode},

                };
                if (!string.IsNullOrEmpty(digitizedTemplateCode))
                {
                    requestParams.Add("digitizedTemplateCode", digitizedTemplateCode);
                }
                var apiEndpoint = "get-config-by-action-code";
                response = await client.GetAsync<GenericResponse<ExternalProviderServiceConfigDto>>(_serviceUri, apiEndpoint, requestParams, null, accessToken);
            }
            catch (Exception ex)
            {

                response = GenericResponse<ExternalProviderServiceConfigDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
