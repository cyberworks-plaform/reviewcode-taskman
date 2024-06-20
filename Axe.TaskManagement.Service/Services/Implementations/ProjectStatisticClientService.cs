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
    public class ProjectStatisticClientService : IProjectStatisticClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public ProjectStatisticClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeReportEndpoint}/project-statistic";
        }

        public async Task<GenericResponse<long>> UpdateProjectStatisticAsync(ProjectStatisticUpdateProgressDto model, string accessToken = null)
        {
            GenericResponse<long> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "update-project-statistic";
                response = await client.PostAsync<GenericResponse<long>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<long>> UpdateMultiProjectStatisticAsync(ProjectStatisticUpdateMultiProgressDto model, string accessToken = null)
        {
            GenericResponse<long> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "update-multi-project-statistic";
                response = await client.PostAsync<GenericResponse<long>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
