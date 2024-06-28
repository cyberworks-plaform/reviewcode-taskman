using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IJobService : IMongoBaseService<Job, JobDto>
    {
        Task<GenericResponse<List<Job>>> UpSertMultiJobAsync(List<JobDto> models);

        Task<GenericResponse<List<InfoJob>>> GetInfoJobs(string accessToken = null);

        Task<GenericResponse<JobDto>> GetProcessingJobById(string id);
        Task<GenericResponse<JobDto>> GetCompleteJobById(string id);

        Task<GenericResponse<List<JobDto>>> GetListJobByDocInstanceId(Guid docInstanceId);

        Task<GenericResponse<List<JobDto>>> GetListJobByDocInstanceIds(List<Guid> docInstanceIds);

        Task<GenericResponse<JobDto>> GetProcessingJobCheckFinalByFileInstanceId(Guid fileInstanceId);
        Task<GenericResponse<JobDto>> GetProcessingJobQACheckFinalByFileInstanceId(Guid fileInstanceId);

        Task<GenericResponse<List<JobDto>>> GetListJob(string actionCode = null, string accessToken = null);

        Task<GenericResponse<List<JobDto>>> GetProactiveListJob(string actionCode = null, Guid? projectTypeInstanceId = null, string accessToken = null);

        Task<GenericResponse<int>> ProcessSegmentLabeling(JobResult result, string accessToken = null);

        Task<GenericResponse<int>> ProcessDataEntry(List<JobResult> result, string accessToken = null);

        Task<GenericResponse<int>> ProcessDataEntryBool(List<JobResult> result, string accessToken = null);

        Task<GenericResponse<int>> ProcessDataCheck(List<JobResult> result, string accessToken = null);

        Task<GenericResponse<int>> ProcessDataConfirm(List<JobResult> result, string accessToken = null);

        Task<GenericResponse<string>> ProcessDataConfirmAuto(ModelInput model, string accessToken = null);

        Task<GenericResponse<string>> ProcessDataConfirmBool(ModelInput model, string accessToken = null);

        Task<GenericResponse<int>> ProcessCheckFinal(JobResult result, string accessToken = null);

        Task<GenericResponse<string>> ProcessSyntheticData(ModelInput model, string accessToken = null);

        Task<GenericResponse<List<JobDto>>> GetListJobByUserProject(Guid userInstanceId, Guid projectInstanceId);

        Task<GenericResponse<long>> GetCountJobByUserProject(Guid userInstanceId, Guid projectInstanceId, Guid? workflowStepInstanceId = null);

        Task<GenericResponse<bool>> CheckUserHasJob(Guid userInstanceId, Guid projectInstanceId, string actionCode = null, short status = (short)EnumJob.Status.Processing);

        Task<GenericResponse<bool>> CheckHasJobWaitingOrProcessingByMultiWfs(DocCheckHasJobWaitingOrProcessingDto model);

        Task<GenericResponse<bool>> CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId, Guid? parallelJobInstanceId);

        Task<GenericResponse<List<JobDto>>> GetJobCompleteByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId, Guid? parallelJobInstanceId);

        Task<GenericResponse<List<JobDto>>> GetJobByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null, short? status = null);

        Task<GenericResponse<List<JobDto>>> GetJobByWfsInstanceIds(Guid docInstanceId, string workflowStepInstanceIds);

        Task<GenericResponse<IEnumerable<SelectItemDto>>> GetDropDownFileCheckFinal(string accessToken);

        Task<GenericResponse<long>> ReCallJobByUser(string accessToken);

        Task<GenericResponse<int>> SkipJobDataEntry(string jobId, string reason);

        Task<GenericResponse<int>> UndoSkipJobDataEntry(string jobIdStr);

        Task<GenericResponse<long>> GetCountJobWaiting(string actionCode, string accessToken);

        Task<GenericResponse<int>> WarningCheckFinal(string jobIdstr, string reason);

        Task<GenericResponse<int>> UndoWarningCheckFinal(string jobIdStr);

        Task<GenericResponse<List<JobDto>>> GetListJobByProjectInstanceId(Guid projectInstanceId);

        Task<GenericResponse<bool>> UpdateValueJob(UpdateValueJob model);

        Task<GenericResponse<bool>> UpdateMultiValueJob(UpdateMultiValueJob model);

        Task<GenericResponse<long>> GetCountJobByUser(Guid userInstanceId, Guid wflsConfig);

        Task<GenericResponse<long>> DeleteMultiByDocAsync(Guid docid);

        Task<GenericResponse<List<JobDto>>> GetListJobCompleteByStatus(Guid projectInstanceId, int status);

        Task<GenericResponse<long>> GetCountJobByStatusActionCode(Guid projectInstanceId, int status = 0, string actionCode = null);

        Task<GenericResponse<List<JobDto>>> GetListJobByProjectAndWorkflowStepInstanceId(Guid projectInstanceId, Guid workflowstepInstanceId);

        Task<GenericResponse<List<JobDto>>> GetListJobByFilterCode(string code, string strDocInstanceids, Guid projectInstanceId);

        Task<GenericResponse<List<JobDto>>> GetListJobByStatusActionCode(Guid projectInstanceId, int status = 0, string actionCode = null);
        Task<GenericResponse<HistoryJobDto>> GetHistoryJobByUser(PagingRequest request, string actionCode, string accessToken);
        Task<GenericResponse<double>> GetFalsePercent(string accessToken);
        Task<GenericResponse<HistoryJobDto>> GetHistoryJobByStep(PagingRequest request, string projectInstanceId, string sActionCodes);
        Task<GenericResponse<PagedList<HistoryUserJobDto>>> GetPagingHistoryUser(PagingRequest request, string accessToken);

        Task<GenericResponse<List<Guid>>> GetListUserInstanceIdByProject(Guid projectInstanceId);

        Task<GenericResponse<List<JobDto>>> GetListCompleteJobByFilePartInstanceId(string strfilePartInstanceId);

        //GET VALUE CHART 

        Task<GenericResponse<List<ErrorDocReportSummary>>> GetErrorDocReportSummary(Guid projectInstanceId, string folderId, string accessToken = null);

        Task<GenericResponse<PagedList<DocErrorDto>>> GetPagingErrorDocByProject(PagingRequest request, Guid projectInstanceId, string folderId, string accessToken = null);

        Task<GenericResponse<bool>> RetryErrorDocs(List<Guid> instanceIds, string accessToken);

        Task<GenericResponse<bool>> RetryAllErrorDocs(Guid projectInstanceId, string accessToken);

        //Task<GenericResponse<bool>> RetryErrorJobByStep(Guid workflowStepInstanceId, long pathId, string accessToken);

        Task<GenericResponse<SelectItemChartDto>> GetTimeNumberJobChart(string startDateStr, string endDateStr);
        Task<GenericResponse<List<SummaryTotalDocPathJob>>> GetSummaryFolder(Guid projectInstanceId, string lstPathId, string accessToken = null);
        //Task<GenericResponse<List<SummaryTotalDocPathJob>>> GetSummaryFolder(Guid projectInstanceId, string lstPathId);
        Task<GenericResponse<List<TotalDocPathJob>>> GetSummaryDoc(Guid projectInstanceId, string path, string docInstanceIds);
        Task<GenericResponse<List<TotalJobProcessingStatistics>>> GetTotalJobProcessingStatistics(Guid projectInstanceId, string startDate = null, string endDate = null);
        Task<GenericResponse<List<TotalJobProcessingStatistics>>> GetTotalJobPaymentStatistics(Guid projectInstanceId);

        Task<GenericResponse<List<ProjectCountExtensionDto>>> GetCountJobInProject(List<Guid?> projectInstanceIds, string strActionCode, string accessToken);

        Task<GenericResponse<bool>> LockJobByPath(Guid projectInstanceId, string pathRelationId, string accessToken = null);
        Task<GenericResponse<List<JobDto>>> GetListJobForUser(ProjectDto project, string actionCode, int metaId, Guid docTypeFieldInstanceId, string parallelInstanceIds,string docPath,Guid batchInstanceId,int numOfRound, string accessToken = null);
        Task<GenericResponse<bool>> UnLockJobByPath(Guid projectInstanceId, string pathRelationId, string accessToken = null);
        Task<GenericResponse<List<CountJobEntity>>> GetCountAllJobByStatus();
        Task<GenericResponse<List<CountJobEntity>>> GetSummaryJobByAction(Guid projectInstanceId, string fromDate, string toDate);
        Task<GenericResponse<List<CountJobEntity>>> GetSummaryDocByAction(Guid projectInstanceId, Guid? wfInstanceId, string fromDate, string toDate, string accessToken = null);
        Task<GenericResponse<WorkSpeedReportEntity>> GetWorkSpeed(Guid? projectInstanceId, Guid? userInstanceId);
        Task<GenericResponse<List<JobByDocDoneEntity>>> GetSummaryJobOfDoneFileByStep(Guid? projectInstanceId, string lastAction);
        Task<GenericResponse<List<JobOfFileEntity>>> GetSummaryJobOfFile(Guid? docInstanceId);
        Task<GenericResponse<int>> SkipJobDataCheck(string jobId, string reason);

        Task<GenericResponse<int>> UndoSkipJobDataCheck(string jobIdStr);
        Task<GenericResponse<PagedListExtension<JobProcessingStatistics>>> GetTotalJobProcessingStatistics_V2(PagingRequest request, bool hasPaging = true);
        Task<GenericResponse> ResyncJobDistribution();
        Task<GenericResponse<int>> ProcessQaCheckFinal(JobResult result, string accessToken = null);
        Task<bool> PublishLogJobEvent(List<Job> jobs, string accessToken);
    }
}
