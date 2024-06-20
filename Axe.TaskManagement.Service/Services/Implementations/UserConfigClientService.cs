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
    public class UserConfigClientService : IUserConfigClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public UserConfigClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/user-config";
        }

        public async Task<GenericResponse<string>> GetValueByCodeAsync(string code, string accessToken = null)
        {
            GenericResponse<string> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-value-by-code?code={code}";
                response = await client.GetAsync<GenericResponse<string>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<bool>> SetValueByCodeAsync(string code, string val, string col = "StringVal", string accessToken = null)
        {
            GenericResponse<bool> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "set-value-by-code";
                var requestParam = new Dictionary<string, string>
                {
                    { "code",  code},
                    { "val",  val},
                    { "col",  col}
                };
                response = await client.PutAsync<GenericResponse<bool>>(_serviceUri, apiEndpoint, null, requestParam, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
