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
    public class ProjectTypeClientService : IProjectTypeClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public ProjectTypeClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/project-type";
        }

        public virtual async Task<GenericResponse<IEnumerable<SelectItemDto>>> GetDropdownExternalAsync(bool onlyActive = true, string accessToken = null)
        {
            GenericResponse<IEnumerable<SelectItemDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "get-dropdown-external";
                var parameters = new Dictionary<string, string>
                {
                    { "onlyActive", onlyActive.ToString()}
                };
                response = await client.GetAsync<GenericResponse<IEnumerable<SelectItemDto>>>(_serviceUri, apiEndpoint, parameters, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<IEnumerable<SelectItemDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Log.Error(ex, ex.Message);
            }

            return response;
        }
    }
}
