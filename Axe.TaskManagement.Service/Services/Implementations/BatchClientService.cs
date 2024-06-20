using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class BatchClientService : IBatchClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public BatchClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/batch";
        }

        public async Task<GenericResponse<BatchDto>> CreateBatch(BatchDto model, string accessToken = null)
        {
            GenericResponse<BatchDto> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "add-v2";
                response = await client.PostAsync<GenericResponse<BatchDto>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<BatchDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
