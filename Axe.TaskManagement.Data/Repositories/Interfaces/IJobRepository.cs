using Axe.TaskManagement.Model.Entities;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.Utility.EntityExtensions;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Data.Repositories.Interfaces
{
    public interface IJobRepository : IMongoBaseRepository<Job>
    {
        Task<List<Job>> UpSertMultiJobAsync(IEnumerable<Job> entities);

        Task<List<Job>> GetJobProcessingByProjectAsync(Guid userInstanceId, string actionCode, Guid? projectTypeInstanceId);

        Task<bool> CheckUserHasJob(Guid userInstanceId, Guid projectInstanceId, string actionCode = null, short status = (short)EnumJob.Status.Processing);

        Task<bool> CheckHasJobByProjectTypeActionCode(Guid projectTypeInstanceId, string actionCode, short status = (short)EnumJob.Status.Waiting);

        Task<bool> CheckHasJobWaitingOrProcessingByIgnoreWfs(Guid docInstanceId, string ignoreActionCode = null, Guid? ignoreWorkflowStepInstanceId = null);

        Task<bool> CheckHasJobWaitingOrProcessingByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null);

        Task<bool> CheckHasJobWaitingOrProcessingByMultiWfs(Guid docInstanceId, List<WorkflowStepInfo> checkWorkflowStepInfos);

        Task<bool> CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId, Guid? parallelJobInstanceId);

        Task<List<Job>> GetJobCompleteByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId, Guid? parallelJobInstanceId);

        Task<bool> CheckHasJobCompleteByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null);

        Task<List<Job>> GetJobByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null, short? status = null);

        Task<List<Job>> GetJobByWfsInstanceIds(Guid docInstanceId, List<Guid> workflowStepInstanceIds);

        Task<List<Job>> GetAllJobByWfs(Guid projectInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null,
            short? status = null, string docPath = null, Guid? batchJobInstanceId = null, short numOfRound = -1, Guid? docInstanceId = null);

        Task<List<Job>> GetJobByDocs(IEnumerable<Guid?> docInstanceIds, string actionCode = null, short? status = null);

        Task<List<Job>> GetPrevJobs(Job crrJob, List<Guid> prevWorkflowStepInstanceIds);

        Task<long> DeleteMultiByDocAsync(Guid docInstanceId);

        Task<List<Job>> GetAllJobAsync(FilterDefinition<Job> filter, SortDefinition<Job> sort, int limit = 10);

        Task<List<ProjectStoreExtension>> GetDistinctProjectOrderByCreatedDate(FilterDefinition<Job> filter);

        Task<List<Guid>> GetDistinctWfsInstanceId(FilterDefinition<Job> filter);

        Task<List<Job>> GetJobDoneByWfsWhenAllDoneBefore(List<WorkflowStepInfo> workflowStepInfos, Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null);

        Task<List<Guid>> GetDistinctUserInstanceId(FilterDefinition<Job> filter);

        Task<PagedList<DocErrorExtension>> GetPagingDocErrorAsync(FilterDefinition<Job> filter, int index = 1, int size = 10);
        Task<PagedListExtension<Job>> GetPagingExtensionAsync(FilterDefinition<Job> filter, SortDefinition<Job> sort = null, int index = 1, int size = 10);
        Task<double> GetFalsePercentAsync(Guid userInstanceId);


        //GET VALUE CHART 
        Task<long> GetTimeNumberJobChart(FilterDefinition<Job> filter);

        Task<List<TotalDocPathJob>> GetSummaryFolder(FilterDefinition<Job> filter);
        Task<List<TotalJobProcessingStatistics>> GetTotalJobProcessingStatistics(FilterDefinition<Job> filter);
        Task<List<TotalJobProcessingStatistics>> TotalJobPaymentStatistics(FilterDefinition<Job> filter);
        Task<List<TotalDocPathJob>> GetSummaryDoc(FilterDefinition<Job> filter);
        Task<List<ProjectCountExtension>> GetCountJobInProject(FilterDefinition<Job> filter);
        Task<List<Job>> GetJobProcessingByUserAsync(Guid userInstanceId, string actionCode, Guid projectInstanceId);

        Task<List<CountJobEntity>> GetCountAllJobByStatus();
        Task<List<CountJobEntity>> GetSummaryJobByAction(Guid projectInstanceId, string fromDate, string toDate);
        Task<List<CountJobEntity>> GetSummaryDocByAction(Guid projectInstanceId, List<WorkflowStepInfo> wfsInfoes,
            List<WorkflowSchemaConditionInfo> wfSchemaInfoes, string fromDate, string toDate);
        Task<WorkSpeedReportEntity> GetWorkSpeed(Guid? projectInstanceId, Guid? userInstanceId);
        Task<List<JobProcessingStatistics>> GetTotalJobProcessingStatistics_V2(FilterDefinition<Job> filter);
        Task<List<JobByDocDoneEntity>> GetSummaryJobOfDoneFileByStep(Guid? projectInstanceId, string lastAction);
        Task<List<JobOfFileEntity>> GetSummaryJobOfFile(Guid? docInstanceId);
        Task<Job> UpdateAndLockRecordAsync(Job entity);
        Task<Job> GetJobByInstanceId(Guid instanceId);
        Task<List<Job>> GetJobsByDocInstanceId(Guid docInstanceId);
        Task<IAsyncCursor<Job>> GetCursorListJobAsync(FilterDefinition<Job> filter, FindOptions<Job> findOption);
    }
}
