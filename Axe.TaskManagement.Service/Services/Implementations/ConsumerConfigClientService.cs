using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Net;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;

namespace Axe.TaskManagement.Service.Services.Implementations;

public class ConsumerConfigClientService : IConsumerConfigClientService
{
    private readonly IBaseHttpClientFactory _clientFatory;
    private readonly string _serviceUri;

    public ConsumerConfigClientService(IBaseHttpClientFactory clientFatory)
    {
        _clientFatory = clientFatory;
        _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/consumer-config";
    }

    public async Task<GenericResponse<ExchangeConfigDto>> GetExchangeConfig(string exchangeName, string accessToken = null)
    {
        GenericResponse<ExchangeConfigDto> response;
        try
        {
            var client = _clientFatory.Create();
            var apiEndpoint = $"get-exchange-config?exchangeName={exchangeName}";
            response = await client.GetAsync<GenericResponse<ExchangeConfigDto>>(_serviceUri, apiEndpoint, null, null, accessToken);
        }
        catch (Exception ex)
        {
            response = GenericResponse<ExchangeConfigDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
        }
        return response;
    }
}