using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Constant.Lib.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/job")]
    [Authorize]
    public class JobController : MongoBaseController<IJobService, Job, JobDto>
    {
        #region Initialize

        public JobController(IJobService service) : base(service)
        {
        }

        #endregion

        [HttpPut]
        [Route("upsert-multi")]
        public virtual async Task<IActionResult> UpSertMultiJob([FromBody] List<JobDto> models)
        {
            if (ModelState.IsValid)
            {
                return ResponseResult(await _service.UpSertMultiJobAsync(models));
            }

            return BadRequest(ModelState);
        }

        [HttpGet]
        [Route("get-info-jobs")]
        public async Task<IActionResult> GetInfoJobs()
        {
            return ResponseResult(await _service.GetInfoJobs(GetBearerToken()));
        }

        [HttpGet]
        [Route("get-processing-job-by-id/{id}")]
        public async Task<IActionResult> GetProcessingJobById(string id)
        {
            return ResponseResult(await _service.GetProcessingJobById(id));
        }

        [HttpGet]
        [Route("get-complete-job-by-id/{id}")]
        public async Task<IActionResult> GetCompleteJobById(string id)
        {
            return ResponseResult(await _service.GetCompleteJobById(id));
        }

        [HttpPost]
        [Route("get-list-job-by-doc-instance-id")]
        public async Task<IActionResult> GetListJobByDocInstanceId(Guid docInstanceId)
        {
            return ResponseResult(await _service.GetListJobByDocInstanceId(docInstanceId));
        }

        [HttpPost]
        [Route("get-list-job-by-doc-instance-ids")]
        public async Task<IActionResult> GetListJobByDocInstanceIds(List<Guid> docInstanceIds)
        {
            return ResponseResult(await _service.GetListJobByDocInstanceIds(docInstanceIds));
        }

        [HttpPost]
        [Route("get-check-final-by-file-instance-id")]
        public async Task<IActionResult> GetListJobCheckFinalByFileInstanceId(Guid fileInstanceId)
        {
            return ResponseResult(await _service.GetProcessingJobCheckFinalByFileInstanceId(fileInstanceId));
        }

        [HttpPost]
        [Route("get-qa-check-final-by-file-instance-id")]
        public async Task<IActionResult> GetListJobQACheckFinalByFileInstanceId(Guid fileInstanceId)
        {
            return ResponseResult(await _service.GetProcessingJobQACheckFinalByFileInstanceId(fileInstanceId));
        }

        [HttpGet]
        [Route("get-list-job")]
        public async Task<IActionResult> GetListJob(string actionCode = null)
        {
            return ResponseResult(await _service.GetListJob(actionCode, GetBearerToken()));
        }

        [HttpPost]
        [Route("get-proactive-list-job")]
        public async Task<IActionResult> GetProactiveListJob(string actionCode = null, Guid? projectTypeInstanceId = null)
        {
            return ResponseResult(await _service.GetProactiveListJob(actionCode, projectTypeInstanceId, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-segment-labeling")]
        public async Task<IActionResult> ProcessSegmentLabeling(JobResult result)
        {
            return ResponseResult(await _service.ProcessSegmentLabeling(result, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-data-entry")]
        public async Task<IActionResult> ProcessDataEntry(List<JobResult> result)
        {
            return ResponseResult(await _service.ProcessDataEntry(result, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-data-entry-bool")]
        public async Task<IActionResult> ProcessDataEntryBool(List<JobResult> result)
        {
            return ResponseResult(await _service.ProcessDataEntryBool(result, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-data-check")]
        public async Task<IActionResult> ProcessDataCheck(List<JobResult> result)
        {
            return ResponseResult(await _service.ProcessDataCheck(result, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-data-confirm")]
        public async Task<IActionResult> ProcessDataConfirm(List<JobResult> result)
        {
            return ResponseResult(await _service.ProcessDataConfirm(result, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-data-confirm-auto")]
        public async Task<IActionResult> ProcessDataConfirmAuto(ModelInput input)
        {
            return ResponseResult(await _service.ProcessDataConfirmAuto(input, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-data-confirm-bool")]
        public async Task<IActionResult> ProcessDataConfirmBool(ModelInput input)
        {
            return ResponseResult(await _service.ProcessDataConfirmBool(input, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-check-final")]
        public async Task<IActionResult> ProcessCheckFinal(JobResult result)
        {
            return ResponseResult(await _service.ProcessCheckFinal(result, GetBearerToken()));
        }

        [HttpPost]
        [Route("process-qa-check-final")]
        public async Task<IActionResult> ProcessQaCheckFinal(JobResult result)
        {
            return ResponseResult(await _service.ProcessQaCheckFinal(result, GetBearerToken()));
        }

        [HttpPut]
        [Route("process-synthetic-data")]
        public async Task<IActionResult> ProcessSyntheticData(ModelInput input)
        {
            return ResponseResult(await _service.ProcessSyntheticData(input, GetBearerToken()));
        }

        [HttpPost]
        [Route("get-list-by-user-project")]
        public async Task<IActionResult> GetListJobByUserProject(Guid userInstanceId, Guid projectInstanceId)
        {
            return ResponseResult(await _service.GetListJobByUserProject(userInstanceId, projectInstanceId));
        }

        [HttpGet]
        [Route("get-count-by-user-project")]
        public async Task<IActionResult> GetCountJobByUserProject(Guid userInstanceId, Guid projectInstanceId, Guid? workflowStepInstanceId = null)
        {
            return ResponseResult(await _service.GetCountJobByUserProject(userInstanceId, projectInstanceId, workflowStepInstanceId));
        }

        [HttpPost]
        [Route("check-user-has-job")]
        public async Task<IActionResult> CheckUserHasJob(Guid userInstanceId, Guid projectInstanceId, string actionCode = null, short status = (short)EnumJob.Status.Processing)
        {
            return ResponseResult(await _service.CheckUserHasJob(userInstanceId, projectInstanceId, actionCode, status));
        }

        // OCR call
        [HttpPost]
        [Route("check-has-job-waiting-or-processing-by-multi-wfs")]
        public async Task<IActionResult> CheckHasJobWaitingOrProcessingByMultiWfs(DocCheckHasJobWaitingOrProcessingDto model)
        {
            return ResponseResult(await _service.CheckHasJobWaitingOrProcessingByMultiWfs(model));
        }

        [HttpGet]
        [Route("check-has-job-waiting-or-processing-by-doc-field-value-and-parallel-job")]
        public async Task<IActionResult> CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId, Guid? parallelJobInstanceId)
        {
            return ResponseResult(await _service.CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(docInstanceId, docFieldValueInstanceId, parallelJobInstanceId));
        }

        [HttpGet]
        [Route("get-job-complete-by-doc-field-value-and-parallel-job")]
        public async Task<IActionResult> GetJobCompleteByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId, Guid? parallelJobInstanceId)
        {
            return ResponseResult(await _service.GetJobCompleteByDocFieldValueAndParallelJob(docInstanceId, docFieldValueInstanceId, parallelJobInstanceId));
        }

        [HttpGet]
        [Route("get-job-by-wfs")]
        public async Task<IActionResult> GetJobByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null, short? status = null)
        {
            return ResponseResult(await _service.GetJobByWfs(docInstanceId, actionCode, workflowStepInstanceId, status));
        }

        [HttpGet]
        [Route("get-job-by-wfs-instance-ids")]
        public async Task<IActionResult> GetJobByWfsInstanceIds(Guid docInstanceId, string workflowStepInstanceIds)
        {
            return ResponseResult(await _service.GetJobByWfsInstanceIds(docInstanceId, workflowStepInstanceIds));
        }

        [HttpGet]
        [Route("get-dropdown-file-check-final")]
        public virtual async Task<IActionResult> GetDropDownFileCheckFinal()
        {
            return ResponseResult(await _service.GetDropDownFileCheckFinal(GetBearerToken()));
        }

        [HttpGet]
        [Route("recall-job")]
        public async Task<IActionResult> ReCallJob()
        {
            return ResponseResult(await _service.ReCallJobByUser(GetBearerToken()));
        }

        [HttpPost]
        [Route("skip-data-entry-job")]
        public async Task<IActionResult> SkipDataEntryJob(string jobId, string reason)
        {
            return ResponseResult(await _service.SkipJobDataEntry(jobId, reason));
        }

        [HttpPost]
        [Route("warning-check-final-job")]
        public async Task<IActionResult> WarningCheckFinal(string jobId, string reason)
        {
            return ResponseResult(await _service.WarningCheckFinal(jobId, reason));
        }

        [HttpPost]
        [Route("undo-skip-data-entry-job")]
        public async Task<IActionResult> UndoSkipDataEntryJob(string jobId)
        {
            return ResponseResult(await _service.UndoSkipJobDataEntry(jobId));
        }

        [HttpPost]
        [Route("undo-warning-check-final-job")]
        public async Task<IActionResult> UndoWarningCheckFinal(string jobId)
        {
            return ResponseResult(await _service.UndoWarningCheckFinal(jobId));
        }

        [HttpGet]
        [Route("get-count-job-waiting")]
        public async Task<IActionResult> GetCountJobWaiting(string actionCode)
        {
            return ResponseResult(await _service.GetCountJobWaiting(actionCode, GetBearerToken()));
        }

        [HttpPost]
        [Route("get-list-job-by-project-instanceId")]
        public async Task<IActionResult> GetListJobByProjectInstanceId(Guid projectInstanceId)
        {
            return ResponseResult(await _service.GetListJobByProjectInstanceId(projectInstanceId));
        }
        [HttpPost]
        [Route("get-count-job-by-user")]
        public async Task<IActionResult> GetCountJobByUser(Guid userInstanceId, Guid wflsConfig)
        {
            return ResponseResult(await _service.GetCountJobByUser(userInstanceId, wflsConfig));
        }

        [HttpDelete]
        [Route("delete-multi-by-doc/{docid}")]
        public async Task<IActionResult> DeleteMultiByDocAsync(Guid docid)
        {
            return ResponseResult(await _service.DeleteMultiByDocAsync(docid));
        }

        [HttpGet]
        [Route("get-jobs-by-project-instanceid/{projectInstanceId}")]
        public async Task<IActionResult> GetJobsByProjectInstanceId(Guid projectInstanceId)
        {
            return ResponseResult(await _service.GetListJobByProjectInstanceId(projectInstanceId));
        }

        [HttpPut]
        [Route("update-value-job")]
        public async Task<IActionResult> UpdateValueJob(UpdateValueJob model)
        {
            return ResponseResult(await _service.UpdateValueJob(model));
        }

        [HttpPut]
        [Route("update-multi-value-job")]
        public async Task<IActionResult> UpdateMultiValueJob(UpdateMultiValueJob model)
        {
            return ResponseResult(await _service.UpdateMultiValueJob(model));
        }

        [HttpPost]
        [Route("get-list-job-complete-by-status")]
        public async Task<IActionResult> GetListJobCompleteByStatus(Guid projectInstanceId, int status)
        {
            return ResponseResult(await _service.GetListJobCompleteByStatus(projectInstanceId, status));
        }

        [HttpPost]
        [Route("get-count-job-by-status-actioncode")]
        public async Task<IActionResult> GetCountJobByStatus(Guid projectInstanceId, int status, string actionCode)
        {
            return ResponseResult(await _service.GetCountJobByStatusActionCode(projectInstanceId, status, actionCode));
        }

        [HttpGet]
        [Route("get-jobs-by-project-and-workflow-step-instanceid")]
        public async Task<IActionResult> GetListJobByProjectAndWorkflowStepInstanceId(Guid projectInstanceId, Guid workflowStepInstanceId)
        {
            return ResponseResult(await _service.GetListJobByProjectAndWorkflowStepInstanceId(projectInstanceId, workflowStepInstanceId));
        }

        [HttpGet]
        [Route("get-jobs-by-filter-code")]
        public async Task<IActionResult> GetListJobByFilterCode(string code, string strDocInstanceids, Guid projectInstanceId)
        {
            return ResponseResult(await _service.GetListJobByFilterCode(code, strDocInstanceids, projectInstanceId));
        }
        [HttpGet]
        [Route("get-jobs-by-status-and-step")]
        public async Task<IActionResult> GetListJobByStatusActionCode(Guid projectInstanceId, int status = 0, string actionCode = null)
        {
            return ResponseResult(await _service.GetListJobByStatusActionCode(projectInstanceId, status, actionCode));
        }

        /// <summary>
        /// PagingRequest nhận các filter 
        /// 1>UserInstanceId: không truyền sẽ lấy theo token
        /// 2>StartDate
        /// 3>EndDate
        /// 4>DocName
        /// 5>Code: code của job
        /// 6>NormalState: 0 là unhappy, 1 là happy
        /// </summary>
        /// <param name="request"></param>
        /// <param name="actionCode"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("get-history-job-by-user")]
        public async Task<IActionResult> GetPagingProject([FromBody] PagingRequest request, string actionCode)
        {
            return ResponseResult(await _service.GetHistoryJobByUser(request, actionCode, GetBearerToken()));
        }

        [HttpPost]
        [Route("get-history-job-by-step")]
        public async Task<IActionResult> GetPagingProject([FromBody] PagingRequest request, string projectInstanceId, string sActionCodes)
        {
            return ResponseResult(await _service.GetHistoryJobByStep(request, projectInstanceId, sActionCodes));
        }

        [HttpPost]
        [Route("get-paging-history-user")]
        public async Task<IActionResult> GetPagingProject([FromBody] PagingRequest request)
        {
            return ResponseResult(await _service.GetPagingHistoryUser(request, GetBearerToken()));
        }

        [HttpGet]
        [Route("get-list-userInstanceId-by-project/{projectInstanceId}")]
        public async Task<IActionResult> GetListUserInstanceIdByProject(Guid projectInstanceId)
        {
            return ResponseResult(await _service.GetListUserInstanceIdByProject(projectInstanceId));
        }

        [HttpGet]
        [Route("get-list-complete-job-by-file-part-instanceId")]
        public async Task<IActionResult> GetListCompleteJobByFilePartInstanceId(string filePartInstanceId)
        {
            return ResponseResult(await _service.GetListCompleteJobByFilePartInstanceId(filePartInstanceId));
        }

        //GET VALUE CHART 
        [HttpGet]
        [Route("get-time-number-job-chart")]
        public async Task<IActionResult> GetTimeNumberJobChart(string startDateStr, string endDateStr)
        {
            return ResponseResult(await _service.GetTimeNumberJobChart(startDateStr, endDateStr));
        }

        #region JOB REPORT

        [HttpPost]
        [Route("get-error-doc-report-summary")]
        public async Task<IActionResult> GetErrorDocReportSummary(Guid projectInstanceId, string folderId)
        {
            return ResponseResult(await _service.GetErrorDocReportSummary(projectInstanceId, folderId, GetBearerToken()));
        }

        [HttpPost]
        [Route("get-paging-error-doc-by-project")]
        public async Task<IActionResult> GetPagingErrorDocByProject([FromBody] PagingRequest request, Guid projectInstanceId, string folderId)
        {
            return ResponseResult(await _service.GetPagingErrorDocByProject(request, projectInstanceId, folderId, GetBearerToken()));
        }

        [HttpPost]
        [Route("retry-error-doc")]
        public async Task<IActionResult> RetryErrorDocs(List<Guid> instanceIds)
        {
            return ResponseResult(await _service.RetryErrorDocs(instanceIds, GetBearerToken()));
        }

        [HttpPost]
        [Route("retry-all-error-doc")]
        public async Task<IActionResult> RetryAllErrorDocs(Guid projectInstanceId)
        {
            return ResponseResult(await _service.RetryAllErrorDocs(projectInstanceId, GetBearerToken()));
        }

        #endregion

        //[HttpPost]
        //[Route("retry-error-job-by-step")]
        //public async Task<IActionResult> RetryErrorJobByStep(Guid workflowStepInstanceId, long pathId)
        //{
        //    return ResponseResult(await _service.RetryErrorJobByStep(workflowStepInstanceId, pathId, GetBearerToken()));
        //}

        [HttpPost]
        [Route("get-summary-folder")]
        public async Task<IActionResult> GetSummaryFolder(Guid projectInstanceId, [FromBody]IdsDto lstPathId)
        {
            return ResponseResult(await _service.GetSummaryFolder(projectInstanceId, lstPathId.Ids));
        }

        [HttpPost]
        [Route("get-summary-doc")]
        public async Task<IActionResult> GetSummaryDoc(Guid projectInstanceId, string path, [FromBody] IdsDto docInstanceIds)
        {
            return ResponseResult(await _service.GetSummaryDoc(projectInstanceId, path, docInstanceIds.Ids));
        }

        [HttpGet]
        [Route("get-total-job-processing-statistics")]
        public async Task<IActionResult> GetTotalJobProcessingStatistics(Guid projectInstanceId, string startDate = null, string endDate = null)
        {
            return ResponseResult(await _service.GetTotalJobProcessingStatistics(projectInstanceId, startDate, endDate));
        }

        [HttpPost]
        [Route("get-total-job-processing-statistics-v2")]
        public async Task<IActionResult> GetTotalJobProcessingStatistics_V2(PagingRequest request, bool hasPaging = true)
        {
            return ResponseResult(await _service.GetTotalJobProcessingStatistics_V2(request, hasPaging));
        }

        [HttpGet]
        [Route("get-total-job-payment-statistics")]
        public async Task<IActionResult> GetTotalJobPaymentStatistics(Guid projectInstanceId)
        {
            return ResponseResult(await _service.GetTotalJobPaymentStatistics(projectInstanceId));
        }

        #region Count job in project
        [Route("get-count-job")]
        [HttpPost]
        public async Task<IActionResult> GetCountJobInProject([FromBody] List<Guid?> projectInstanceIds, string actionCode)
        {
            return ResponseResult(await _service.GetCountJobInProject(projectInstanceIds, actionCode, GetBearerToken()));
        }

        [Route("get-jobs-by-user")]
        [HttpPost]

        public async Task<IActionResult> GetListJobForUser([FromBody] ProjectDto project, string actionCode, int inputType, Guid docTypeFieldInstanceId, string parallelInstanceIds,string docPath,Guid batchInstanceId, int numOfRound)
        {
            return ResponseResult(await _service.GetListJobForUser(project, actionCode, inputType, docTypeFieldInstanceId, parallelInstanceIds,docPath,batchInstanceId, numOfRound, GetBearerToken()));
        }
        #endregion

        [HttpPut]
        [Route("lock-job-by-path")]
        public async Task<IActionResult> LockJobByPath(Guid projectInstanceId, string pathRelationId)
        {
            return ResponseResult(await _service.LockJobByPath(projectInstanceId, pathRelationId, GetBearerToken()));
        }
        [HttpPut]
        [Route("unlock-job-by-path")]
        public async Task<IActionResult> UnLockJobByPath(Guid projectInstanceId, string pathRelationId)
        {
            return ResponseResult(await _service.UnLockJobByPath(projectInstanceId, pathRelationId, GetBearerToken()));
        }

        [HttpGet]
        [Route("get-count-all-job-by-status")]
        public async Task<IActionResult> GetCountAllJobByStatus()
        {
            return ResponseResult(await _service.GetCountAllJobByStatus());
        }

        [HttpGet]
        [Route("get-summary-job-by-action")]
        public async Task<IActionResult> GetSummaryJobByAction(Guid projectInstanceId, string fromDate, string toDate)
        {
            return ResponseResult(await _service.GetSummaryJobByAction(projectInstanceId, fromDate, toDate));
        }

        [HttpGet]
        [Route("get-summary-doc-by-action")]
        public async Task<IActionResult> GetSummaryDocByAction(Guid projectInstanceId, Guid? wfInstanceId, string fromDate, string toDate, string accessToken = null)
        {
            return ResponseResult(await _service.GetSummaryDocByAction(projectInstanceId, wfInstanceId,fromDate, toDate, accessToken));
        }

        [HttpGet]
        [Route("get-work-speed")]
        public async Task<IActionResult> GetWorkSpeed(Guid? projectInstanceId, Guid? userInstanceId)
        {
            return ResponseResult(await _service.GetWorkSpeed(projectInstanceId, userInstanceId));
        }

        [HttpGet]
        [Route("get-summary-job-by-step-done-file")]
        public async Task<IActionResult> GetSummaryJobOfDoneFileByStep(Guid? projectInstanceId, string lastAction)
        {
            return ResponseResult(await _service.GetSummaryJobOfDoneFileByStep(projectInstanceId, lastAction));
        }

        [HttpGet]
        [Route("get-summary-job-of-file")]
        public async Task<IActionResult> GetSummaryJobOfFile(Guid? docInstanceId)
        {
            return ResponseResult(await _service.GetSummaryJobOfFile(docInstanceId));
        }

        [HttpPost]
        [Route("skip-data-check-job")]
        public async Task<IActionResult> SkipDataCheckJob(string jobId, string reason)
        {
            return ResponseResult(await _service.SkipJobDataCheck(jobId, reason));
        }

        [HttpPost]
        [Route("undo-skip-data-check-job")]
        public async Task<IActionResult> UndoSkipDataCheckJob(string jobId)
        {
            return ResponseResult(await _service.UndoSkipJobDataCheck(jobId));
        }

        [HttpPost]
        [Route("resync-job-distribution")]
        public async Task<IActionResult> ResyncJobDistribution()
        {
            return ResponseResult(await _service.ResyncJobDistribution());
        }
    }
}
