using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class DocClientService : IDocClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly IBaseHttpClientFactory _clientSyncMetaRelationFatory;
        private readonly string _serviceSyncMetaUri;
        private readonly string _serviceUri;

        public DocClientService(IBaseHttpClientFactory clientFatory, IBaseHttpClientFactory clientSyncMetaRelationFatory)
        {
            _clientFatory = clientFatory;
            _clientSyncMetaRelationFatory = clientSyncMetaRelationFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/doc";
            _serviceSyncMetaUri = $"{ApiDomain.AxeCoreEndpoint}/sync-meta-relation";
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
        /// <summary>
        /// Hàm này sẽ lấy giá trị từ Docs.FinalValue của một DocInstance
        /// Nếu chỉ cần lấy danh sách các DocItem mà không cần value thì sử dụng class DocTypeField
        /// </summary>
        /// <param name="instanceId"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
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
        /// <summary>
        /// Hàm này sẽ lấy một danh sách các DocItem được chuyển đổi từ Docs.FinalValue 
        /// Nếu chỉ cần lấy danh sách các DocItem mà không cần value thì sử dụng class DocTypeField
        /// </summary>
        /// <param name="instanceIds"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
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
                response = await client.GetAsync<GenericResponse<PathStatusDto>>(_serviceUri, apiEndpoint, requestParam, null, accessToken: accessToken);
                if (!response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<PathStatusDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.Data.ToString(), ex.Message);
                Log.Error(ex, ex.Message);
            }
            return response;
        }
        public async Task<GenericResponse<List<SyncMetaRelationDto>>> GetAllSyncMetaRelationAsync(string accessToken = null)
        {
            GenericResponse<List<SyncMetaRelationDto>> response;
            try
            {
                var client = _clientSyncMetaRelationFatory.Create();
                var apiEndpoint = "get-all"; ;
                response = await client.GetAsync<GenericResponse<List<SyncMetaRelationDto>>>(_serviceSyncMetaUri, apiEndpoint, null, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                    throw new Exception("Không có kết nối");
                }

                return response;

            }
            catch (Exception ex)
            {
                response = GenericResponse<List<SyncMetaRelationDto>>.ResultWithError((int)HttpStatusCode.BadRequest, "Có lỗi đã xảy ra", "Có lỗi đã xảy ra");
                Log.Error(ex, ex.Message);
            }

            return response;
        }
        public async Task<GenericResponse<SyncMetaRelationDto>> GetSyncMetaRelationByIdAsync(long Id, string accessToken = null)
        {
            GenericResponse<SyncMetaRelationDto> response;
            try
            {
                var client = _clientSyncMetaRelationFatory.Create();
                var apiEndpoint = $"/{Id}"; ;
                response = await client.GetAsync<GenericResponse<SyncMetaRelationDto>>(_serviceSyncMetaUri, apiEndpoint, null, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                    throw new Exception("Không có kết nối");
                }

                return response;

            }
            catch (Exception ex)
            {
                response = GenericResponse<SyncMetaRelationDto>.ResultWithError((int)HttpStatusCode.BadRequest, "Có lỗi đã xảy ra", "Có lỗi đã xảy ra");
                Log.Error(ex, ex.Message);
            }

            return response;
        }
        public async Task<GenericResponse<List<DocDto>>> GetListDocByDocInstanceIds(List<Guid> lstInstanceIds, string accessToken = null)
        {
            GenericResponse<List<DocDto>> response;
            try
            {
                var client = _clientFatory.Create();
                //var encodedInstanceIds = WebUtility.UrlEncode(lstInstanceIds);
                var apiEndpoint = $"get-list-doc-by-docInstanceIds";
                response = await client.PostAsync<GenericResponse<List<DocDto>>>(_serviceUri, apiEndpoint, lstInstanceIds, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<DocDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<string>> GetPathName(string docPath, string accessToken = null)
        {
            GenericResponse<string> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-path-name/";
                var param = new Dictionary<string, string>
                {
                    { "path" ,  docPath }
                };
                response = await client.GetAsync<GenericResponse<string>>(_serviceUri, apiEndpoint, param, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<string>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
        public async Task<GenericResponse<int>> UpdateFinalValue(DocUpdateFinalValueEvent model, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "update-final-value";
                response = await client.PostAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, model, null, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
        public async Task<GenericResponse<DocDto>> GetByInstanceIdAsync(Guid instanceId, string accessToken = null)
        {
            GenericResponse<DocDto> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-by-instance/{instanceId}";
                response = await client.GetAsync<GenericResponse<DocDto>>(_serviceUri, apiEndpoint, null, null, accessToken);
                if (response != null && !response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<DocDto>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Log.Error(ex, ex.Message);
            }


            return response;
        }
        public async Task<GenericResponse<Dictionary<string, string>>> GetMultiPathNameByMultiDocPath(string docPaths, string accessToken = null)
        {
            GenericResponse<Dictionary<string, string>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-multi-path-name-by-multi-doc-path";
                var requestParam = new Dictionary<string, string>
                {
                    { "docPaths", docPaths }
                };
                response = await client.GetAsync<GenericResponse<Dictionary<string, string>>>(_serviceUri, apiEndpoint, requestParam, null, accessToken);
                if (!response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<Dictionary<string, string>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Log.Error(ex, ex.Message);
            }
            return response;
        }
        public async Task<GenericResponse<List<DocPathDto>>> GetListPath(int projectId, string accessToken = null)
        {
            GenericResponse<List<DocPathDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = $"get-list-path/{projectId}";
                response = await client.GetAsync<GenericResponse<List<DocPathDto>>>(_serviceUri, apiEndpoint, null, null, accessToken);
                if (!response.Success)
                {
                    Log.Error(response.Message);
                    Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<DocPathDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Log.Error(ex, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> ChangeCurrentWorkFlowStepInfo(Guid docInstanceId, long wfsId, short wfsStatus, Guid? wfsInstanceId, bool? qaStatus, string qaNote, short? numOfRound, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "change-current-work-flow-step-info";
                var requestParam = new Dictionary<string, string>
                {
                    { "docInstanceId",  docInstanceId.ToString()},
                    { "wfsId",  wfsId.ToString()},
                    { "wfsStatus",  wfsStatus.ToString()},
                    { "wfsInstanceId",  wfsInstanceId.ToString()},
                    { "qaStatus",  qaStatus.ToString()},
                    { "qaNote",  qaNote.ToString()},
                    { "numOfRound",  numOfRound.ToString()}
                };
                response = await client.PutAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, null, requestParam, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }

        public async Task<GenericResponse<int>> ChangeMultiCurrentWorkFlowStepInfo(string docInstanceIds, long wfsId, short wfsStatus, string accessToken = null)
        {
            GenericResponse<int> response;
            try
            {
                var client = _clientFatory.Create();
                var apiEndpoint = "change-multi-current-work-flow-step-info";
                var requestParam = new Dictionary<string, string>
                {
                    { "wfsId",  wfsId.ToString()},
                    { "wfsStatus",  wfsStatus.ToString()}
                };
                var model = new { InstanceIds = docInstanceIds };
                response = await client.PutAsync<GenericResponse<int>>(_serviceUri, apiEndpoint, model, requestParam, null, accessToken);
            }
            catch (Exception ex)
            {
                response = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
