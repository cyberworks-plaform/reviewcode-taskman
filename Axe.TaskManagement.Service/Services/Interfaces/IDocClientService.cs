﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IDocClientService
    {
        Task<GenericResponse<int>> ChangeStatus(Guid instanceId, short newStatus = (short)EnumDoc.Status.Processing, string accessToken = null);

        Task<GenericResponse<int>> ChangeStatusMulti(string instanceIds, short newStatus = (short)EnumDoc.Status.Processing, string accessToken = null);
        Task<GenericResponse<List<DocItem>>> GetDocItemByDocInstanceId(Guid instanceId, string accessToken = null);
        Task<GenericResponse<List<GroupDocItem>>> GetGroupDocItemByDocInstanceIds(string instanceIds, string accessToken = null);

        Task<GenericResponse<bool>> CheckLockDoc(Guid docInstanceId, string accessToken = null);

        Task<GenericResponse<List<DocLockStatusDto>>> CheckLockDocs(string instanceIds, string accessToken = null);

        Task<GenericResponse<PathStatusDto>> GetStatusPath(Guid projectInstanceId, Guid syncTypeInstanceId, string path, string accessToken = null);
        Task<GenericResponse<List<SyncMetaRelationDto>>> GetAllSyncMetaRelationAsync(string accessToken = null);
        Task<GenericResponse<List<DocDto>>> GetListDocByDocInstanceIds(List<Guid> lstInstanceIds, string accessToken = null);
        Task<GenericResponse<string>> GetPathName(string docPath, string accessToken = null);
        Task<GenericResponse<DocDto>> GetByInstanceIdAsync(Guid instanceId, string accessToken = null);
        Task<GenericResponse<int>> UpdateFinalValue(DocUpdateFinalValueEvent model, string accessToken = null);
        Task<GenericResponse<Dictionary<string, string>>> GetMultiPathNameByMultiDocPath(string docPaths, string accessToken = null);
        Task<GenericResponse<List<DocPathDto>>> GetListPath(int projectId, string accessToken = null);
        Task<GenericResponse<int>> ChangeCurrentWorkFlowStepInfo(Guid docInstanceId, long wfsId, short wfsStatus, Guid? wfsInstanceId, bool? qaStatus, string qaNote, short? numOfRound, string accessToken = null);
        Task<GenericResponse<int>> ChangeMultiCurrentWorkFlowStepInfo(string docInstanceIds, long wfsId, short wfsStatus, string accessToken = null);
    }
}
