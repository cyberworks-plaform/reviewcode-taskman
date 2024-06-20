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
    public class ProjectClientService : IProjectClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public ProjectClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/project";
        }

        public async Task<GenericResponse<ProjectDto>> GetByInstanceIdAsync(Guid instanceId, string accessToken = null)
        {
            GenericResponse<ProjectDto> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-by-instance/{instanceId}";
                response = await client.GetAsync<GenericResponse<ProjectDto>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<ProjectDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
