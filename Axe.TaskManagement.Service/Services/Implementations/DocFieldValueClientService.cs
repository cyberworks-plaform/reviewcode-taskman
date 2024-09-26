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
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class DocFieldValueClientService : IDocFieldValueClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public DocFieldValueClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/doc-field-value";
        }

        public async Task<GenericResponse<IEnumerable<DocFieldValueDto>>> GetByDocTypeFieldInstanceIds(Guid docInstanceId, string docTypeFieldIntanceIds, string accessToken = null)
        {
            GenericResponse<IEnumerable<DocFieldValueDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "get-by-doc-type-field-instance-ids";
                var request = new
                {
                    DocInstanceId = docInstanceId.ToString(),
                    DocTypeFieldIntanceIds = docTypeFieldIntanceIds
                };
                response = await client.PostAsync<GenericResponse<IEnumerable<DocFieldValueDto>>>(_serviceUri, apiEndpoint, request, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<IEnumerable<DocFieldValueDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> GetCountOfExpectedByDocInstanceId(Guid docInstanceId, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-count-of-expected-by-doc-instance-id/{docInstanceId}";
                response = await client.GetAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> DeleteByDocTypeFieldInstanceIds(Guid docInstanceId, string docTypeFieldIntanceIds, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "delete-by-doc-type-field-instance-ids";
                var request = new
                {
                    DocInstanceId = docInstanceId.ToString(),
                    DocTypeFieldIntanceIds = docTypeFieldIntanceIds
                };
                response = await client.PostAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, request, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<DocFieldValueDto>> GetByInstanceId(Guid instanceId, string accessToken = null)
        {
            GenericResponse<DocFieldValueDto> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-by-instance/{instanceId}";
                response = await client.GetAsync<GenericResponse<DocFieldValueDto>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<DocFieldValueDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> UpdateMulti(List<DocFieldValueDto> docFieldValues, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "update-multi";
                response = await client.PutAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, docFieldValues, null, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }

        public async Task<GenericResponse<int>> UpdateMultiValue(DocFieldValueUpdateMultiValueEvent model, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "update-multi-values";
                response = await client.PostAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return response;
        }
    }
}
