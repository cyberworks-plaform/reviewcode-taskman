using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.Dtos;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class TransactionClientService : ITransactionClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public TransactionClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxePaymentEndpoint}/transaction";
        }

        public async Task<GenericResponse<long>> AddTransactionAsync(TransactionAddDto model, string accessToken = null)
        {
            GenericResponse<long> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "add-transaction";
                response = await client.PostAsync<GenericResponse<long>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> AddMultiTransactionAsync(TransactionAddMultiDto model, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "add-multi-transaction";
                response = await client.PostAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
