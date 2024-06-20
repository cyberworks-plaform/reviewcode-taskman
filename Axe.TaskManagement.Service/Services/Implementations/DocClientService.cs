using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Serilog;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class DocClientService : IDocClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public DocClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/doc";
        }

        public async Task<GenericResponse<int>> ChangeStatus(Guid instanceId, short newStatus = (short)EnumDoc.Status.Processing, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "change-status";
                var requestParam = new Dictionary<string, string>
                {
                    { "instanceId",  instanceId.ToString()},
                    { "newStatus",  newStatus.ToString()}
                };
                response = await client.PutAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, null, requestParam, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> ChangeStatusMulti(string instanceIds, short newStatus = 2, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "change-status-multi";
                var requestParam = new Dictionary<string, string>
                {
                  
                    { "newStatus",  newStatus.ToString()}
                };
                var model = new { InstanceIds = instanceIds };
                response = await client.PutAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, model, requestParam, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<DocItem>>> GetDocItemByDocInstanceId(Guid instanceId, string accessToken = null)
        {
            GenericResponse<List<DocItem>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-doc-item-by-doc-instance-id/{instanceId}";
                response = await client.GetAsync<GenericResponse<List<DocItem>>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<DocItem>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<GroupDocItem>>> GetGroupDocItemByDocInstanceIds(string instanceIds, string accessToken = null)
        {
            GenericResponse<List<GroupDocItem>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "get-group-doc-item-by-doc-instanceids";
                var model = new { InstanceIds = instanceIds };
                response = await client.PostAsync<GenericResponse<List<GroupDocItem>>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<GroupDocItem>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<bool>> CheckLockDoc(Guid docInstanceId, string accessToken = null)
        {
            GenericResponse<bool> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"check-lock-doc/{docInstanceId}";
                response = await client.GetAsync<GenericResponse<bool>>(_serviceUri, apiEndpoint, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<bool>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<List<DocLockStatusDto>>> CheckLockDocs(string instanceIds, string accessToken = null)
        {
            GenericResponse<List<DocLockStatusDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "check-lock-docs";
                var model = new { InstanceIds = instanceIds };
                response = await client.PostAsync<GenericResponse<List<DocLockStatusDto>>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<DocLockStatusDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        /// <summary>
        /// Lấy thông tin tổng hợp số lượng file theo path
        /// </summary>
        /// <param name="projectInstanceId"></param>
        /// <param name="syncTypeInstanceId"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public async Task<GenericResponse<PathStatusDto>> GetStatusPath(Guid projectInstanceId, Guid syncTypeInstanceId, string path, string accessToken = null)
        {
            GenericResponse<PathStatusDto> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "get-status-path";
                var requestParam = new Dictionary<string, string>
                {
                    { "projectInstanceId", projectInstanceId.ToString() },
                    { "syncTypeInstanceId", syncTypeInstanceId.ToString() },
                    { "path", path }
                };
                response = await client.GetAsync<GenericResponse<PathStatusDto>>(_serviceUri, apiEndpoint, requestParam, null, accessToken : accessToken);
                if (!response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<PathStatusDto>.ResultWithError((int)HttpStatusCode.BadRequest,ex.Data.ToString(),ex.Message);
                Log.Error(ex, ex.Message);
            }
            return response;
        }
    }
}
