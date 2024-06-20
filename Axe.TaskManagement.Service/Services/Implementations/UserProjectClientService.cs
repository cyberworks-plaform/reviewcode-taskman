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
    public class UserProjectClientService : IUserProjectClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public UserProjectClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/user-project";
        }

        public async Task<GenericResponse<Guid>> GetPrimaryUserInstanceIdByProject(Guid projectInstanceId, string accessToken = null)
        {
            GenericResponse<Guid> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-primary-user-instance-id-by-project/{projectInstanceId}";
                response = await client.GetAsync<GenericResponse<Guid>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex.StackTrace, ex.Message);
                response = GenericResponse<Guid>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<IEnumerable<Guid>>> GetUserInstanceIdsByProject(Guid projectInstanceId, string accessToken = null)
        {
            GenericResponse<IEnumerable<Guid>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-user-instance-ids-by-project/{projectInstanceId}";
                response = await client.GetAsync<GenericResponse<IEnumerable<Guid>>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<IEnumerable<Guid>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
