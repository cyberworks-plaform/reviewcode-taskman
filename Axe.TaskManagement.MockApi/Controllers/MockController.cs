using System;
using System.Collections.Generic;
using Axe.TaskManagement.Service.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;
using Axe.Utility.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Constant.Lib.Dtos;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http;
using MongoDB.Driver;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using MongoDB.Bson;
using Ce.Constant.Lib.Definitions;
using System.Net.Http.Json;
using Axe.TaskManagement.MockApi.Dto;
using Axe.Utility.Helpers;

namespace Axe.TaskManagement.MockApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class MockController : InfrastructureController
    {
        private readonly IJobService _jobService;
        private readonly ITaskService _taskService;
        private readonly IProjectClientService _projectClientService;
        private readonly IHttpClientFactory _clientFactory;
        private readonly IJobRepository _jobRepos;

        public MockController(IHttpClientFactory httpClientFactory
            ,IJobService jobService, IProjectClientService projectClientService, ITaskService taskService, IJobRepository jobRepository)
        {
            _jobService = jobService;
            _projectClientService = projectClientService;
            _taskService = taskService;
            _jobRepos = jobRepository;
            _clientFactory = httpClientFactory;
        }

        [Route("update-project-type-instance-id")]
        [HttpGet]
        public async Task<IActionResult> UpdateProjectTypeInstanceId()
        {
            GenericResponse<int> result = null;
            var jobsResult = await _jobService.GetAllAsync(false);
            if (jobsResult != null && jobsResult.Success && jobsResult.Data.Any())
            {
                var jobs = jobsResult.Data;
                foreach (var job in jobs)
                {
                    if (job.ProjectInstanceId.HasValue)
                    {
                        var projectResult =
                            await _projectClientService.GetByInstanceIdAsync(job.ProjectInstanceId.GetValueOrDefault(), GetBearerToken());
                        if (projectResult.Success && projectResult.Data != null)
                        {
                            job.ProjectTypeInstanceId = projectResult.Data.ProjectTypeInstanceId;
                        }
                    }
                }

                result = await _jobService.UpdateMultiAsync(jobs);
            }

            return ResponseResult(result);
        }

        //[Route("update-task")]
        //[HttpGet]
        //public async Task<IActionResult> UpdateTask()
        //{
        //    GenericResponse<int> result = GenericResponse<int>.ResultWithData(0);
        //    var jobsResult = await _jobService.GetAllAsync(false);
        //    if (jobsResult != null && jobsResult.Success && jobsResult.Data.Any())
        //    {
        //        var jobs = jobsResult.Data.ToList();

        //        var tasks = jobs.GroupBy(x => new
        //        {
        //            x.ProjectTypeInstanceId, x.ProjectInstanceId, x.FileInstanceId, x.DocInstanceId, x.DocName,
        //            x.WorkflowInstanceId
        //        }).Select(grp => new TaskDto
        //        {
        //            FileInstanceId = grp.Key.FileInstanceId,
        //            DocInstanceId = grp.Key.DocInstanceId,
        //            DocName = grp.Key.DocName,
        //            ProjectTypeInstanceId = grp.Key.ProjectTypeInstanceId,
        //            ProjectInstanceId = grp.Key.ProjectInstanceId,
        //            WorkflowInstanceId = grp.Key.WorkflowInstanceId,
        //            Progress = null,
        //            Status =
        //                grp.Count(c =>
        //                    c.Status == (short) EnumJob.Status.Processing ||
        //                    c.Status == (short)EnumJob.Status.Complete) == 0
        //                    ? (short) EnumTask.Status.Created
        //                    : (grp.Count(c => c.Status == (short)EnumJob.Status.Waiting || c.Status == (short) EnumTask.Status.Processing) == 0
        //                        ? (short) EnumTask.Status.Complete
        //                        : (short) EnumTask.Status.Processing)
        //        }).ToList();
        //        result = await _taskService.AddMultiAsync(tasks);
        //    }

        //    return ResponseResult(result);
        //}

        [HttpGet]
        [Route("test-upsert_jobs")]
        [AllowAnonymous]
        public async Task<IActionResult> TestUpsertJobs(int param = 1)
        {
            var models = new List<JobDto>
            {
                new JobDto
                {
                    Code = "J64554",
                    DocInstanceId = new Guid("f5770ce1-aeca-49a2-a405-0dc8b0a07f1d"),
                    DocName = "KS.2016.01.2016-03-15.052.pdf",
                    DocCreatedDate = DateTime.UtcNow,
                    DocTypeFieldInstanceId = null,
                    DocTypeFieldName = null,
                    DocTypeFieldSortOrder = 0,
                    DocFieldValueInstanceId = null,
                    InputType = 0,
                    Format = null,
                    RandomSortOrder = 747204698,
                    ProjectTypeInstanceId = new Guid("14c2552d-08d3-47dc-90da-433c4cba75c8"),
                    ProjectInstanceId = new Guid("dfb95bd8-66c2-4238-955a-4ea7f0b8cf65"),
                    WorkflowInstanceId = new Guid("5b4fc41c-0c40-4994-afe4-acdb8074d2b1"),
                    WorkflowStepInstanceId = new Guid("3933499e-3ed6-42e5-a151-fc3a65f00c92"),
                    ActionCode = "CheckFinal",
                    UserInstanceId = new Guid("e3ce8791-d30d-4989-b26d-70fc0cd9d864"),
                    FileInstanceId = new Guid("79a92337-50ee-4729-809b-38cb10dbdbc8"),
                    FilePartInstanceId = null,
                    CoordinateArea = null,
                    Value = null,
                    Price = 783,
                    Status = 1
                }
            };
            var models2 = new List<JobDto>
            {
                new JobDto
                {
                    Code = "J64555",
                    DocInstanceId = new Guid("f5770ce1-aeca-49a2-a405-0dc8b0a07f1d"),
                    DocName = "KS.2016.01.2016-03-15.052.pdf",
                    DocCreatedDate = DateTime.UtcNow,
                    DocTypeFieldInstanceId = null,
                    DocTypeFieldName = null,
                    DocTypeFieldSortOrder = 0,
                    DocFieldValueInstanceId = null,
                    InputType = 0,
                    Format = null,
                    RandomSortOrder = 413012432,
                    ProjectTypeInstanceId = new Guid("14c2552d-08d3-47dc-90da-433c4cba75c8"),
                    ProjectInstanceId = new Guid("dfb95bd8-66c2-4238-955a-4ea7f0b8cf65"),
                    WorkflowInstanceId = new Guid("5b4fc41c-0c40-4994-afe4-acdb8074d2b1"),
                    WorkflowStepInstanceId = new Guid("3933499e-3ed6-42e5-a151-fc3a65f00c92"),
                    ActionCode = "CheckFinal",
                    UserInstanceId = new Guid("e3ce8791-d30d-4989-b26d-70fc0cd9d864"),
                    FileInstanceId = new Guid("79a92337-50ee-4729-809b-38cb10dbdbc8"),
                    FilePartInstanceId = null,
                    CoordinateArea = null,
                    Value = null,
                    Price = 783,
                    Status = 1
                }
            };

            var models3 = new List<JobDto>
            {
                new JobDto
                {
                    Code = "J64554",
                    DocInstanceId = new Guid("f5770ce1-aeca-49a2-a405-0dc8b0a07f1d"),
                    DocName = "KS.2016.01.2016-03-15.052.pdf",
                    DocCreatedDate = DateTime.UtcNow,
                    DocTypeFieldInstanceId = null,
                    DocTypeFieldName = null,
                    DocTypeFieldSortOrder = 0,
                    DocFieldValueInstanceId = null,
                    InputType = 0,
                    Format = null,
                    RandomSortOrder = 747204698,
                    ProjectTypeInstanceId = new Guid("14c2552d-08d3-47dc-90da-433c4cba75c8"),
                    ProjectInstanceId = new Guid("dfb95bd8-66c2-4238-955a-4ea7f0b8cf65"),
                    WorkflowInstanceId = new Guid("5b4fc41c-0c40-4994-afe4-acdb8074d2b1"),
                    WorkflowStepInstanceId = new Guid("3933499e-3ed6-42e5-a151-fc3a65f00c92"),
                    ActionCode = "CheckFinal",
                    UserInstanceId = new Guid("e3ce8791-d30d-4989-b26d-70fc0cd9d864"),
                    FileInstanceId = new Guid("79a92337-50ee-4729-809b-38cb10dbdbc8"),
                    FilePartInstanceId = null,
                    CoordinateArea = null,
                    Value = null,
                    Price = 783,
                    Status = 1
                },
                new JobDto
                {
                    Code = "J64555",
                    DocInstanceId = new Guid("f5770ce1-aeca-49a2-a405-0dc8b0a07f1d"),
                    DocName = "KS.2016.01.2016-03-15.052.pdf",
                    DocCreatedDate = DateTime.UtcNow,
                    DocTypeFieldInstanceId = null,
                    DocTypeFieldName = null,
                    DocTypeFieldSortOrder = 0,
                    DocFieldValueInstanceId = null,
                    InputType = 0,
                    Format = null,
                    RandomSortOrder = 413012432,
                    ProjectTypeInstanceId = new Guid("14c2552d-08d3-47dc-90da-433c4cba75c8"),
                    ProjectInstanceId = new Guid("dfb95bd8-66c2-4238-955a-4ea7f0b8cf65"),
                    WorkflowInstanceId = new Guid("5b4fc41c-0c40-4994-afe4-acdb8074d2b1"),
                    WorkflowStepInstanceId = new Guid("3933499e-3ed6-42e5-a151-fc3a65f00c92"),
                    ActionCode = "CheckFinal",
                    UserInstanceId = new Guid("e3ce8791-d30d-4989-b26d-70fc0cd9d864"),
                    FileInstanceId = new Guid("79a92337-50ee-4729-809b-38cb10dbdbc8"),
                    FilePartInstanceId = null,
                    CoordinateArea = null,
                    Value = null,
                    Price = 783,
                    Status = 1
                }
            };

            if (param == 1)
            {
                return ResponseResult(await _jobService.UpSertMultiJobAsync(models));
            }
            else if (param == 2)
            {
                return ResponseResult(await _jobService.UpSertMultiJobAsync(models2));
            }
            else if (param == 3)
            {
                return ResponseResult(await _jobService.UpSertMultiJobAsync(models3));
            }
            else
            {
                await _jobService.UpSertMultiJobAsync(models);
                await _jobService.UpSertMultiJobAsync(models2);
                return ResponseResult(new GenericResponse());
            }
        }


        [HttpGet]
        [Route("fix-data-doc-path")]
        [AllowAnonymous]
        public async Task<IActionResult> FixDataDocPath(string accessToken)
        {
            var filterNotExist_SyncTypeInstanceId = Builders<Job>.Filter.Exists(x => x.SyncTypeInstanceId, false);
            var filterIsNull_SyncTypeInstanceId = Builders<Job>.Filter.Eq(x => x.SyncTypeInstanceId, null);
            var filterNotNull_Docpath  = Builders<Job>.Filter.Ne(x => x.DocPath, BsonString.Empty);    
            
            var filterCheck = (filterNotExist_SyncTypeInstanceId | filterIsNull_SyncTypeInstanceId) & filterNotNull_Docpath;

            var url = ApiDomain.AxeCoreEndpoint + "/sync-meta-relation";
            while (await _jobRepos.CountAsync(filterCheck) > 0)
            {
                Console.WriteLine("Running");
                var job = await _jobRepos.FindFirstAsync(filterCheck);
                if (job != null)
                {
                    var relationPath = job.DocPath;
                    var relationId = job.SyncMetaId;

                    var client = _clientFactory.CreateClient();
                    client.DefaultRequestHeaders.Add("Authorization",$"Bearer {accessToken}");
                    var response = await client.GetFromJsonAsync<GenericResponse<Axe.TaskManagement.MockApi.Dto.SyncMetaRelationDto>>($"{url}/{relationId}");

                    if (response == null  || !response.Success)
                    {
                        break;
                    }
                    var relationDto = response.Data;
                    if (relationDto == null)
                    {
                        break;
                    }
                    var syncMetaId = SyncPathHelper.GetSyncMetaId(relationDto.Path);
                    var filter =   Builders<Job>.Filter.Eq(x => x.SyncMetaId, relationId);
                    await _jobRepos.UpdateManyAsync(filter, Builders<Job>.Update
                        .Set(x => x.SyncMetaId, syncMetaId)
                        .Set(x => x.DocPath, relationDto.Path)
                        .Set(x => x.SyncTypeInstanceId, relationDto.SyncTypeInstanceId)
                   );
                }
            }

            return new OkObjectResult("Ok");
        }
    }
}
