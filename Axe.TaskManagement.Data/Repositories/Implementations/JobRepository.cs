using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.Utility.Definitions;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using Ce.Constant.Lib.Dtos;
using MongoDB.Bson;
using MongoDB.Driver;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class JobRepository : MongoBaseRepository<Job>, IJobRepository
    {
        public JobRepository(IMongoContext context) : base(context)
        {
        }

        public async Task<List<Job>> UpSertMultiJobAsync(IEnumerable<Job> entities)
        {
            var result = new List<Job>();
            var updateOneModels = new List<UpdateOneModel<Job>>();
            foreach (var entity in entities)
            {
                result.Add(entity);

                var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, entity.DocInstanceId);
                //var filter2 = Builders<Job>.Filter.Eq(x => x.TaskInstanceId, entity.TaskInstanceId);
                var filter2 = Builders<Job>.Filter.Eq(x => x.DocTypeFieldInstanceId, entity.DocTypeFieldInstanceId);
                var filter3 = Builders<Job>.Filter.Eq(x => x.DocFieldValueInstanceId, entity.DocFieldValueInstanceId);
                var filter4 = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, entity.ProjectInstanceId);
                var filter5 = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, entity.WorkflowStepInstanceId);
                var filter6 = Builders<Job>.Filter.Eq(x => x.Value, entity.Value);
                //var filter6 = Builders<Job>.Filter.Eq(x => x.ActionCode, entity.ActionCode);
                //var filter6 = Builders<Job>.Filter.Eq(x => x.TenantId, entity.TenantId);
                var filter7 = Builders<Job>.Filter.Eq(x => x.ShareJobSortOrder, entity.ShareJobSortOrder);
                var filter8 = Builders<Job>.Filter.Eq(x => x.Status, entity.Status);
                var filter = filter1 & filter2 & filter3 & filter4 & filter5 & filter6 & filter7 & filter8;

                var updateValue = Builders<Job>.Update
                    //.Set(s => s.Id, entity.Id)
                    .Set(s => s.InstanceId, entity.InstanceId)
                    .Set(s => s.Code, entity.Code)
                    .Set(s => s.TurnInstanceId, entity.TurnInstanceId)
                    .Set(s => s.TaskId, entity.TaskId)
                    .Set(s => s.TaskInstanceId, entity.TaskInstanceId)
                    .Set(s => s.DocInstanceId, entity.DocInstanceId)
                    .Set(s => s.DocName, entity.DocName)
                    .Set(s => s.DocCreatedDate, entity.DocCreatedDate)
                    .Set(s => s.SyncTypeInstanceId, entity.SyncTypeInstanceId)
                    .Set(s => s.DocPath, entity.DocPath)
                    .Set(s => s.SyncMetaId, entity.SyncMetaId)
                    .Set(s => s.DigitizedTemplateInstanceId, entity.DigitizedTemplateInstanceId)
                    .Set(s => s.DigitizedTemplateCode, entity.DigitizedTemplateCode)
                    .Set(s => s.DocTypeFieldInstanceId, entity.DocTypeFieldInstanceId)
                    .Set(s => s.DocTypeFieldName, entity.DocTypeFieldName)
                    .Set(s => s.DocTypeFieldSortOrder, entity.DocTypeFieldSortOrder)
                    .Set(s => s.DocFieldValueInstanceId, entity.DocFieldValueInstanceId)
                    .Set(s => s.InputType, entity.InputType)
                    .Set(s => s.PrivateCategoryInstanceId, entity.PrivateCategoryInstanceId)
                    .Set(s => s.IsMultipleSelection, entity.IsMultipleSelection)
                    .Set(s => s.Format, entity.Format)
                    .Set(s => s.MinLength, entity.MinLength)
                    .Set(s => s.MaxLength, entity.MaxLength)
                    .Set(s => s.MinValue, entity.MinValue)
                    .Set(s => s.MaxValue, entity.MaxValue)
                    .Set(s => s.RandomSortOrder, entity.RandomSortOrder)
                    .Set(s => s.ProjectTypeInstanceId, entity.ProjectTypeInstanceId)
                    .Set(s => s.ProjectInstanceId, entity.ProjectInstanceId)
                    .Set(s => s.WorkflowInstanceId, entity.WorkflowInstanceId)
                    .Set(s => s.WorkflowStepInstanceId, entity.WorkflowStepInstanceId)
                    .Set(s => s.ActionCode, entity.ActionCode)
                    .Set(s => s.UserInstanceId, entity.UserInstanceId)
                    .Set(s => s.Value, entity.Value)
                    .Set(s => s.OldValue, entity.OldValue)
                    .Set(s => s.Input, entity.Input)
                    .Set(s => s.Output, entity.Output)
                    .Set(s => s.FileInstanceId, entity.FileInstanceId)
                    .Set(s => s.FilePartInstanceId, entity.FilePartInstanceId)
                    .Set(s => s.CoordinateArea, entity.CoordinateArea)
                    .Set(s => s.ReceivedDate, entity.ReceivedDate)
                    .Set(s => s.DueDate, entity.DueDate)
                    .Set(s => s.CreatedDate, entity.CreatedDate)
                    .Set(s => s.CreatedBy, entity.CreatedBy)
                    .Set(s => s.LastModificationDate, entity.LastModificationDate)
                    .Set(s => s.LastModifiedBy, entity.LastModifiedBy)
                    .Set(s => s.StartWaitingDate, entity.StartWaitingDate)
                    .Set(s => s.Note, entity.Note)
                    .Set(s => s.IsIgnore, entity.IsIgnore)
                    .Set(s => s.ReasonIgnore, entity.ReasonIgnore)
                    .Set(s => s.IsWarning, entity.IsWarning)
                    .Set(s => s.ReasonWarning, entity.ReasonWarning)
                    .Set(s => s.HasChange, entity.HasChange)
                    .Set(s => s.OldValue, entity.OldValue)
                    .Set(s => s.Input, entity.Input)
                    .Set(s => s.Output, entity.Output)
                    .Set(s => s.Price, entity.Price)
                    .Set(s => s.ClientTollRatio, entity.ClientTollRatio)
                    .Set(s => s.WorkerTollRatio, entity.WorkerTollRatio)
                    .Set(s => s.ShareJobSortOrder, entity.ShareJobSortOrder)
                    .Set(s => s.IsParallelJob, entity.IsParallelJob)
                    .Set(s => s.ParallelJobInstanceId, entity.ParallelJobInstanceId)
                    .Set(s => s.TenantId, entity.TenantId)
                    .Set(s => s.RightStatus, entity.RightStatus)
                    .Set(s => s.Status, entity.Status);

                updateOneModels.Add(new UpdateOneModel<Job>(filter, updateValue));
            }

            var data = await UpSertMultiAsync(updateOneModels);

            // Update Id
            if (data.ModifiedCount == 0)
            {
                for (int i = 0; i < result.Count; i++)
                {
                    if (data.Upserts.Count > i)
                    {
                        result[i].Id = new ObjectId(data.Upserts[i].Id.ToString());
                    }
                }
            }

            return result;
        }

        public async Task<List<Job>> GetJobProcessingByProjectAsync(Guid userInstanceId, string actionCode, Guid? projectTypeInstanceId)
        {
            FilterDefinition<Job> filter;
            var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId);
            var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter3 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            if (userInstanceId != Guid.Empty)
            {
                filter = filter1 & filter2 & filter3;
            }
            else
            {
                filter = filter2 & filter3;
            }
            if (projectTypeInstanceId != null && projectTypeInstanceId != Guid.Empty)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.ProjectTypeInstanceId, projectTypeInstanceId);
            }

            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        public async Task<List<Job>> GetJobProcessingByUserAsync(Guid userInstanceId, string actionCode, Guid projectInstanceId)
        {
            FilterDefinition<Job> filter;
            var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId);
            var filter2 = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
            var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter4 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            if (userInstanceId != Guid.Empty)
                filter = filter1 & filter2 & filter3 & filter4;
            else
                filter = filter2 & filter3 & filter4;

            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        public async Task<bool> CheckUserHasJob(Guid userInstanceId, Guid projectInstanceId, string actionCode = null, short status = (short)EnumJob.Status.Processing)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.UserInstanceId, userInstanceId);
            var filter2 = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
            var filter3 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter4 = Builders<Job>.Filter.Ne(x => x.Status, status);
            var filter = filter1 & filter2 & filter3 & filter4;
            var existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
            return existedJob != null;
        }

        public async Task<bool> CheckHasJobByProjectTypeActionCode(Guid projectTypeInstanceId, string actionCode, short status = (short)EnumJob.Status.Waiting)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.ProjectTypeInstanceId, projectTypeInstanceId);
            var filter2 = Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter3 = Builders<Job>.Filter.Eq(x => x.Status, status);
            var filter = filter1 & filter2 & filter3;
            var existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
            return existedJob != null;
        }

        public async Task<bool> CheckHasJobWaitingOrProcessingByIgnoreWfs(Guid docInstanceId, string ignoreActionCode = null, Guid? ignoreWorkflowStepInstanceId = null)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter21 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);
            var filter22 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            var filter3 = string.IsNullOrEmpty(ignoreActionCode)
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Ne(x => x.ActionCode, ignoreActionCode);
            var filter4 = ignoreWorkflowStepInstanceId == null
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Ne(x => x.WorkflowStepInstanceId, ignoreWorkflowStepInstanceId);
            var filter = filter1 & (filter21 | filter22) & filter3 & filter4;
            var existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
            return existedJob != null;
        }

        public async Task<bool> CheckHasJobWaitingOrProcessingByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter21 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);
            var filter22 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            var filter3 = string.IsNullOrEmpty(actionCode)
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter4 = workflowStepInstanceId == null
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId);
            var filter = filter1 & (filter21 | filter22) & filter3 & filter4;
            var existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
            return existedJob != null;
        }

        public async Task<List<Job>> GetJobDoneByWfsWhenAllDoneBefore(List<WorkflowStepInfo> workflowStepInfos, Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null)
        {

            //Check all done before
            var filterDocInstanceId = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filterNotComplete = Builders<Job>.Filter.Ne(x => x.Status, (short)EnumJob.Status.Complete);
            var hasWfsBefore = false;
            var filterWfs = Builders<Job>.Filter.Empty;
            foreach (var wfs in workflowStepInfos)
            {
                if (hasWfsBefore)
                {
                    filterWfs = filterWfs | Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, wfs.InstanceId);
                }
                else
                {
                    filterWfs = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, wfs.InstanceId);
                }
                hasWfsBefore = true;
            }
            var filterCheckAllBefore = filterNotComplete & filterDocInstanceId & filterWfs;
            var existedJob = await DbSet.Find(filterCheckAllBefore).Limit(1).SingleOrDefaultAsync();
            if (existedJob != null)
            {
                return new List<Job>();
            }
            //Get Job done
            var filterActionCode = string.IsNullOrEmpty(actionCode)
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter4 = workflowStepInstanceId == null
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId);
            var filter = filterDocInstanceId & filterActionCode & filter4;
            var doneJob = await DbSet.Find(filter).ToListAsync();
            if (doneJob.Any(x => x.Status != (short)EnumJob.Status.Complete))
            {
                return new List<Job>();
            }

            return doneJob;
        }

        public async Task<bool> CheckHasJobWaitingOrProcessingByMultiWfs(Guid docInstanceId, List<WorkflowStepInfo> checkWorkflowStepInfos)
        {
            Job existedJob;
            var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter21 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);
            var filter22 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            if (checkWorkflowStepInfos == null || checkWorkflowStepInfos.Count == 0)
            {
                existedJob = await DbSet.Find(filter1 & (filter21 | filter22)).Limit(1).SingleOrDefaultAsync();
                return existedJob != null;
            }


            foreach (var wfs in checkWorkflowStepInfos)
            {
                var filter3 = string.IsNullOrEmpty(wfs.ActionCode)
                    ? Builders<Job>.Filter.Empty
                    : Builders<Job>.Filter.Eq(x => x.ActionCode, wfs.ActionCode);
                var filter4 = Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, wfs.InstanceId);
                var filter = filter1 & (filter21 | filter22) & filter3 & filter4;
                existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
                if (existedJob != null)
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> CheckHasJobWaitingOrProcessingByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId,
            Guid? parallelJobInstanceId)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter21 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);
            var filter22 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Processing);
            var filter3 = Builders<Job>.Filter.Eq(x => x.DocFieldValueInstanceId, docFieldValueInstanceId);
            var filter4 = Builders<Job>.Filter.Eq(x => x.ParallelJobInstanceId, parallelJobInstanceId);
            var filter = filter1 & (filter21 | filter22) & filter3 & filter4;
            var existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
            if (existedJob != null)
            {
                return true;
            }

            return false;
        }

        public async Task<List<Job>> GetJobCompleteByDocFieldValueAndParallelJob(Guid docInstanceId, Guid? docFieldValueInstanceId,
            Guid? parallelJobInstanceId)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter2 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete);
            var filter3 = Builders<Job>.Filter.Eq(x => x.DocFieldValueInstanceId, docFieldValueInstanceId);
            var filter4 = Builders<Job>.Filter.Eq(x => x.ParallelJobInstanceId, parallelJobInstanceId);
            var filter = filter1 & filter2 & filter3 & filter4;
            return await DbSet.Find(filter).ToListAsync();
        }

        public async Task<bool> CheckHasJobCompleteByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null)
        {
            var filter1 = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter2 = Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Complete);
            var filter3 = string.IsNullOrEmpty(actionCode)
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            var filter4 = workflowStepInstanceId == null
                ? Builders<Job>.Filter.Empty
                : Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId);
            var filter = filter1 & filter2 & filter3 & filter4;
            var existedJob = await DbSet.Find(filter).Limit(1).SingleOrDefaultAsync();
            return existedJob != null;
        }

        public async Task<List<Job>> GetJobByWfs(Guid docInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null, short? status = null)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            if (!string.IsNullOrEmpty(actionCode))
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            }
            if (workflowStepInstanceId != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId);
            }

            if (status != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.Status, status);
            }
            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        public async Task<List<Job>> GetJobByWfsInstanceIds(Guid docInstanceId, List<Guid> workflowStepInstanceIds)
        {
            var lstWorkflowStepInstanceIds = workflowStepInstanceIds.Select(x => (Guid?)x).ToList();
            var filter = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId) &
                         Builders<Job>.Filter.In(x => x.WorkflowStepInstanceId, lstWorkflowStepInstanceIds);
            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        /// <summary>
        /// Lấy danh sách các job theo 1 step
        /// Update 17-09-2024: bắt buộc filter theo projectId
        /// </summary>
        /// <param name="actionCode"></param>
        /// <param name="workflowStepInstanceId"></param>
        /// <param name="status"></param>
        /// <param name="docPath"></param>
        /// <param name="batchJobInstanceId"></param>
        /// <param name="numOfRound"></param>
        /// <param name="docInstanceId"></param>
        /// <returns></returns>
        public async Task<List<Job>> GetAllJobByWfs(Guid projectInstanceId, string actionCode = null, Guid? workflowStepInstanceId = null,
            short? status = null, string docPath = null, Guid? batchJobInstanceId = null, short numOfRound = -1, Guid? docInstanceId = null)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, projectInstanceId);
            
            filter =filter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);

            if (workflowStepInstanceId != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId);
            }

            if (status != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.Status, status);
            }

            if (!string.IsNullOrEmpty(docPath))
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.DocPath, docPath);
            }

            if (batchJobInstanceId != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.BatchJobInstanceId, batchJobInstanceId);
            }

            if (numOfRound >= 0)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
            }

            if (docInstanceId != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            }

            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        public async Task<List<Job>> GetJobByDocs(IEnumerable<Guid?> docInstanceIds, string actionCode = null, short? status = null)
        {
            var filter = Builders<Job>.Filter.In(x => x.DocInstanceId, docInstanceIds);
            if (!string.IsNullOrEmpty(actionCode))
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
            }

            if (status != null)
            {
                filter = filter & Builders<Job>.Filter.Eq(x => x.Status, status);
            }
            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        public async Task<List<Job>> GetPrevJobs(Job crrJob, List<Guid> prevWorkflowStepInstanceIds)
        {
            var lstPrevWorkflowStepInstanceIds = prevWorkflowStepInstanceIds.Select(x => (Guid?)x).ToList();
            var filter = Builders<Job>.Filter.Eq(x => x.DocInstanceId, crrJob.DocInstanceId) &
                         Builders<Job>.Filter.Eq(x => x.DocFieldValueInstanceId, crrJob.DocFieldValueInstanceId) &
                         Builders<Job>.Filter.In(x => x.WorkflowStepInstanceId, lstPrevWorkflowStepInstanceIds);
            var data = DbSet.Find(filter);
            return await data.ToListAsync();
        }

        public async Task<List<Job>> GetJobsByDocInstanceId(Guid docInstanceId)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var jobs = DbSet.Find(filter);
            return await jobs.ToListAsync();
        }


        public async Task<long> DeleteMultiByDocAsync(Guid docInstanceId)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var deleteJobs = await DbSet.DeleteManyAsync(filter);
            return deleteJobs.DeletedCount;
        }

        public async Task<List<Job>> GetAllJobAsync(FilterDefinition<Job> filter, SortDefinition<Job> sort, int limit = 10)
        {
            IFindFluent<Job, Job> data;
            if (sort == null)
            {
                data = limit <= 0 ? DbSet.Find(filter) : DbSet.Find(filter).Limit(limit);
            }
            else
            {
                data = limit <= 0 ? DbSet.Find(filter).Sort(sort) : DbSet.Find(filter).Sort(sort).Limit(limit);
            }

            return await data.ToListAsync();
        }

        public async Task<List<ProjectStoreExtension>> GetDistinctProjectOrderByCreatedDate(FilterDefinition<Job> filter)
        {
            var aggregate = DbSet.Aggregate();
            aggregate = aggregate.Match(filter);
            var grouped = await aggregate.Group(x => x.ProjectInstanceId,
                g => new
                {
                    ProjectInstanceId = g.Key,
                    g.First().ProjectTypeInstanceId,
                    g.First().WorkflowInstanceId,
                    g.First().WorkflowStepInstanceId,
                    g.First().CreatedDate,
                    g.First().TenantId
                }).SortBy(_ => _.CreatedDate).ToListAsync();
            return grouped.Where(x => x.ProjectInstanceId.HasValue).Select(i => new ProjectStoreExtension
            {
                ProjectTypeInstanceId = i.ProjectTypeInstanceId,
                ProjectInstanceId = i.ProjectInstanceId.Value,
                WorkflowInstanceId = i.WorkflowInstanceId,
                WorkflowStepInstanceId = i.WorkflowStepInstanceId,
                TenantId = i.TenantId
            }).ToList();
        }

        public async Task<List<Guid>> GetDistinctWfsInstanceId(FilterDefinition<Job> filter)
        {
            var grouped = await DbSet.DistinctAsync<Guid>("WorkflowStepInstanceId", filter);
            return await grouped.ToListAsync();
        }

        public async Task<List<Guid>> GetDistinctUserInstanceId(FilterDefinition<Job> filter)
        {
            var grouped = await DbSet.DistinctAsync<Guid>(nameof(Job.UserInstanceId), filter);
            return await grouped.ToListAsync();
        }

        // TODO Turning
        public async Task<PagedList<DocErrorExtension>> GetPagingDocErrorAsync(FilterDefinition<Job> filter, int index = 1, int size = 10)
        {
            var aggregate = DbSet.Aggregate();
            aggregate = aggregate.Match(filter);
            var grouped = await aggregate.Group(x => x.DocInstanceId,
                g => new
                {
                    DocInstanceId = g.Key,
                    g.First().DocName,
                    g.First().DocPath,
                    g.First().DocCreatedDate,
                    g.First().WorkflowStepInstanceId,
                    g.First().ActionCode,
                    g.First().LastModificationDate,
                    g.First().RetryCount,
                    g.First().TenantId
                }).SortBy(_ => _.DocCreatedDate).ToListAsync();

            var allData = grouped.Where(x => x.DocInstanceId.HasValue).Select(i => new DocErrorExtension
            {
                DocInstanceId = i.DocInstanceId.Value,
                DocName = i.DocName,
                DocPath = i.DocPath,
                DocCreatedDate = i.DocCreatedDate,
                WorkflowStepInstanceId = i.WorkflowStepInstanceId,
                ActionCode = i.ActionCode,
                LastModificationDate = i.LastModificationDate,
                RetryCount = i.RetryCount,
                TenantId = i.TenantId
            }).ToList();

            var data = allData.Skip((index - 1) * size).Take(size).ToList();

            var totalfilter = data.Count;
            var total = allData.Count;

            return new PagedList<DocErrorExtension>
            {
                Data = data,
                PageIndex = index,
                PageSize = size,
                TotalCount = total,
                TotalFilter = (int)total,
                TotalPages = (int)Math.Ceiling((decimal)totalfilter / size)
            };
        }

        public async Task<long> GetTimeNumberJobChart(FilterDefinition<Job> filter)
        {
            var aggregate = DbSet.Aggregate();
            aggregate = aggregate.Match(filter);
            var bsonGroup = new BsonDocument
            {
                {"_id", new BsonDocument{ { "turn_instance_id", "$turn_instance_id" } } },
                {"turn_instance_id", new BsonDocument{ { "$first", "$turn_instance_id" } } },
                {"last_modification_date", new BsonDocument{ { "$first", "$last_modification_date" } } },
                {"received_date", new BsonDocument{ { "$first", "$received_date" } } },
            };

            var bsonProject = new BsonDocument
            {
                {"turn_instance_id", 1 },
                {"time_ht", new BsonDocument("$subtract",new BsonArray() { "$last_modification_date", "$received_date"})}
            };

            var bsonGroupSum = new BsonDocument
            {
                {"_id", "_id" },
                {"sum_time", new BsonDocument{ { "$sum", "$time_ht" } } }
            };
            var response = await aggregate
                .Group(bsonGroup)
                .Project(bsonProject)
                .Group(bsonGroupSum)
                .FirstOrDefaultAsync();

            if (response != null)
            {
                var timeWork = response["sum_time"].AsInt64;
                return timeWork;
            }
            var result = 0;
            return result;
        }

        public async Task<List<TotalDocPathJob>> GetSummaryFolder(FilterDefinition<Job> filter) //Hàm mới
        {
            var serializerRegistry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
            var documentSerializer = serializerRegistry.GetSerializer<Job>();
            var f = filter.Render(documentSerializer, serializerRegistry).ToBsonDocument();

            var bson = new BsonDocument[] {
            new BsonDocument("$match", f),
            new BsonDocument("$project",
                new BsonDocument {
                    { "doc_instance_id", 1 },
                    { "doc_path", 1 },
                    { "status", 1 },
                    { "action_code", 1 },
                    { "work_flow_step_instance_id", 1 },
                    { "doc_type_field_sort_order", 1 },
                    //{ "sync_type_instance_id", 1 },
                    { "batch_job_instance_id", 1 },
                    { "batch_name", 1 },
                    { "num_of_round", 1 },
                    { "isNL", new BsonDocument("$cond",
                        new BsonArray {
                            new BsonDocument("$eq", new BsonArray { "$action_code", "DataEntry" }),
                            1,
                            0
                        })
                    },
                    { "isOCR", new BsonDocument("$cond",
                        new BsonArray {
                            new BsonDocument("$eq", new BsonArray { "$action_code", "OCR" }),
                            1,
                            0
                        })
                    },
                    { "isDoubleSort", new BsonDocument("$cond",
                        new BsonArray {
                            new BsonDocument("$and", new BsonArray {
                                new BsonDocument("$eq", new BsonArray { "$doc_type_field_sort_order", 1 }),
                                new BsonDocument("$eq", new BsonArray { "$action_code", "DataEntry" })
                            }),
                            1,
                            0
                        })
                    }
                }),
            new BsonDocument("$group",
                new BsonDocument {
                    { "_id", "$batch_job_instance_id" },
                    { "totalNL", new BsonDocument("$sum", "$isNL") },
                    { "totalOCR", new BsonDocument("$sum", "$isOCR") },
                    { "totalDoubleSort", new BsonDocument("$sum", "$isDoubleSort") },
                    { "z", new BsonDocument("$push",
                        new BsonDocument {
                            { "doc_path", "$doc_path" },
                            { "status", "$status" },
                            { "action_code", "$action_code" },
                            { "doc_instance_id", "$doc_instance_id" },
                            { "work_flow_step_instance_id", "$work_flow_step_instance_id" },
                            //{ "sync_type_instance_id", "$sync_type_instance_id" },
                            //{ "batch_job_instance_id", "$batch_job_instance_id" },
                            { "batch_name", "$batch_name" },
                            { "num_of_round", "$num_of_round" }
                        })
                    }
                }),
            new BsonDocument("$unwind", "$z"),
            new BsonDocument("$project",
                new BsonDocument {
                    { "batch_job_instance_id", 1 },
                    { "totalNL", new BsonDocument("$cond",
                        new BsonArray {
                            new BsonDocument("$eq", new BsonArray { "$totalDoubleSort", 0 }),
                            0,
                            new BsonDocument("$divide", new BsonArray { "$totalNL", "$totalDoubleSort" })
                        })
                    },
                    { "totalOCR", 1 },
                    { "totalDoubleSort", 1 },
                    //{ "batch_job_instance_id", "$z.batch_job_instance_id" },
                    { "batch_name", "$z.batch_name" },
                    { "num_of_round", "$z.num_of_round" },
                    { "doc_path", "$z.doc_path" },
                    { "action_code", "$z.action_code" },
                    { "doc_instance_id", "$z.doc_instance_id" },
                    { "work_flow_step_instance_id", "$z.work_flow_step_instance_id" },
                    //{ "sync_type_instance_id", "$z.sync_type_instance_id" },
                    { "status", "$z.status" }
                }),
            new BsonDocument("$group",
                new BsonDocument {
                    { "_id", new BsonDocument {
                        { "action_code", "$action_code" },
                        { "work_flow_step_instance_id", "$work_flow_step_instance_id" },
                        //{ "sync_type_instance_id", "$sync_type_instance_id" },
                        { "doc_instance_id", "$doc_instance_id" },
                        { "doc_path", "$doc_path" },
                        { "status", "$status" }
                    }},
                    { "totalNL", new BsonDocument("$first", "$totalNL") },
                    { "totalOCR", new BsonDocument("$first", "$totalOCR") },
                    { "action_code", new BsonDocument("$first", "$action_code") },
                    { "work_flow_step_instance_id", new BsonDocument("$first", "$work_flow_step_instance_id") },
                    { "doc_instance_id", new BsonDocument("$first", "$doc_instance_id") },
                    { "doc_path", new BsonDocument("$first", "$doc_path") },
                    { "status", new BsonDocument("$first", "$status") },
                    { "total", new BsonDocument("$sum", 1) },
                    //{ "batch_job_instance_id", new BsonDocument("$first", "$batch_job_instance_id") },
                    //{ "sync_type_instance_id", new BsonDocument("$first", "$sync_type_instance_id") },
                    { "batch_name", new BsonDocument("$first", "$batch_name") },
                    { "num_of_round", new BsonDocument("$first", "$num_of_round") }
                }),
            new BsonDocument("$project",
                new BsonDocument {
                    { "action_code", 1 },
                    { "work_flow_step_instance_id", 1 },
                    { "doc_instance_id", 1 },
                    { "doc_path", 1 },
                    { "status", 1 },
                    { "totalNL", 1 },
                    { "totalOCR", 1 },
                    { "total", 1 },
                    //{ "sync_type_instance_id", 1 },
                    { "batch_job_instance_id", 1 },
                    { "batch_name", 1 },
                    { "num_of_round", 1 }
                })
        };

            var result = await DbSet.Aggregate<BsonDocument>(bson).ToListAsync();
            var response = result.Select(x => new TotalDocPathJob
            {
                ActionCode = x["action_code"].ToString(),
                DocInstanceId = x["doc_instance_id"].AsGuid,
                WorkflowStepInstanceId = x["work_flow_step_instance_id"].AsGuid,
                //SyncTypeInstanceId = x["sync_type_instance_id"].AsGuid,
                Path = x["doc_path"].ToString(),
                Status = !string.IsNullOrEmpty(x["status"].ToString()) ? short.Parse(x["status"].ToString()) : (short)0,
                Total = !string.IsNullOrEmpty(x["total"].ToString()) ? int.Parse(x["total"].ToString()) : 0,
                //BatchJobInstanceId = x["batch_job_instance_id"].AsGuid,
                BatchName = x.GetValue("batch_name", "").ToString(), // Lấy batch_name
                NumOfRound = !string.IsNullOrEmpty(x["num_of_round"].ToString()) ? int.Parse(x["num_of_round"].ToString()) : 0, // Lấy num_of_round
            }).ToList();

            return response;
        }


        public async Task<List<TotalDocPathJob>> GetSummaryFolder_old(FilterDefinition<Job> filter)
        {
            var serializerRegistry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
            var documentSerializer = serializerRegistry.GetSerializer<Job>();
            var f = filter.Render(documentSerializer, serializerRegistry).ToBsonDocument();
            var bson = new BsonDocument[] {
                new BsonDocument("$match",
                    f),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "doc_instance_id",
                            1
                        }, {
                            "doc_path",
                            1
                        }, {
                            "status",
                            1
                        }, {
                            "action_code",
                            1
                        }, {
                            "work_flow_step_instance_id",
                            1
                        }, {
                            "doc_type_field_sort_order",
                            1
                        }, {
                            "isNL",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$action_code",
                                                "DataEntry"
                                            }),
                                        1,
                                        0
                                })
                        }, {
                            "isOCR",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$action_code",
                                                "OCR"
                                            }),
                                        1,
                                        0
                                })
                        }, {
                            "isDoubleSort",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$and",
                                            new BsonArray {
                                                new BsonDocument("$eq",
                                                        new BsonArray {
                                                            "$doc_type_field_sort_order",
                                                            1
                                                        }),
                                                    new BsonDocument("$eq",
                                                        new BsonArray {
                                                            "$action_code",
                                                            "DataEntry"
                                                        })
                                            }),
                                        1,
                                        0
                                })
                        }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            "$doc_instance_id"
                        }, {
                            "totalNL",
                            new BsonDocument("$sum", "$isNL")
                        }, {
                            "totalOCR",
                            new BsonDocument("$sum", "$isOCR")
                        }, {
                            "totalDoubleSort",
                            new BsonDocument("$sum", "$isDoubleSort")
                        }, {
                            "z",
                            new BsonDocument("$push",
                                new BsonDocument {
                                    {
                                        "doc_path",
                                        "$doc_path"
                                    }, {
                                        "status",
                                        "$status"
                                    }, {
                                        "action_code",
                                        "$action_code"
                                    }, {
                                        "work_flow_step_instance_id",
                                        "$work_flow_step_instance_id"
                                    }
                                })
                        }
                    }),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "doc_instance_id",
                            "$_id"
                        }, {
                            "totalNL",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$totalDoubleSort",
                                                0
                                            }),
                                        0,
                                        new BsonDocument("$divide",
                                            new BsonArray {
                                                "$totalNL",
                                                "$totalDoubleSort"
                                            })
                                })
                        }, {
                            "totalOCR",
                            1
                        }, {
                            "totalDoubleSort",
                            1
                        }, {
                            "z",
                            1
                        }
                    }),
                new BsonDocument("$unwind",
                    new BsonDocument("path", "$z")),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "doc_instance_id",
                            1
                        }, {
                            "totalNL",
                            1
                        }, {
                            "totalOCR",
                            1
                        }, {
                            "doc_path",
                            "$z.doc_path"
                        }, {
                            "action_code",
                            "$z.action_code"
                        }, {
                            "work_flow_step_instance_id",
                            "$z.work_flow_step_instance_id"
                        }, {
                            "status",
                            "$z.status"
                        }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            new BsonDocument {
                                {
                                    "action_code",
                                    "$action_code"
                                }, {
                                    "work_flow_step_instance_id",
                                    "$work_flow_step_instance_id"
                                }, {
                                    "doc_instance_id",
                                    "$doc_instance_id"
                                }, {
                                    "doc_path",
                                    "$doc_path"
                                }, {
                                    "status",
                                    "$status"
                                }
                            }
                        }, {
                            "totalNL",
                            new BsonDocument("$first", "$totalNL")
                        }, {
                            "totalOCR",
                            new BsonDocument("$first", "$totalOCR")
                        }, {
                            "action_code",
                            new BsonDocument("$first", "$action_code")
                        }, {
                            "work_flow_step_instance_id",
                            new BsonDocument("$first", "$work_flow_step_instance_id")
                        }, {
                            "doc_instance_id",
                            new BsonDocument("$first", "$doc_instance_id")
                        }, {
                            "doc_path",
                            new BsonDocument("$first", "$doc_path")
                        }, {
                            "status",
                            new BsonDocument("$first", "$status")
                        }, {
                            "total",
                            new BsonDocument("$sum", 1)
                        }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            new BsonDocument {
                                {
                                    "action_code",
                                    "$action_code"
                                }, {
                                    "work_flow_step_instance_id",
                                    "$work_flow_step_instance_id"
                                }, {
                                    "doc_instance_id",
                                    "$doc_instance_id"
                                }, {
                                    "doc_path",
                                    "$doc_path"
                                }
                            }
                        }, {
                            "totalNL",
                            new BsonDocument("$first", "$totalNL")
                        }, {
                            "totalOCR",
                            new BsonDocument("$first", "$totalOCR")
                        }, {
                            "action_code",
                            new BsonDocument("$first", "$action_code")
                        }, {
                            "work_flow_step_instance_id",
                            new BsonDocument("$first", "$work_flow_step_instance_id")
                        }, {
                            "doc_instance_id",
                            new BsonDocument("$first", "$doc_instance_id")
                        }, {
                            "doc_path",
                            new BsonDocument("$first", "$doc_path")
                        }, {
                            "status",
                            new BsonDocument("$first", "$status")
                        }, {
                            "total",
                            new BsonDocument("$first", "$total")
                        }, {
                            "totalSum",
                            new BsonDocument("$sum", 1)
                        }
                    }),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "action_code",
                            1
                        }, {
                            "work_flow_step_instance_id",
                            1
                        }, {
                            "doc_path",
                            1
                        }, {
                            "doc_instance_id",
                            1
                        }, {
                            "totalNL",
                            1
                        }, {
                            "totalOCR",
                            1
                        }, {
                            "status",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$gt",
                                            new BsonArray {
                                                "$totalSum",
                                                1
                                            }),
                                        "2",
                                        new BsonDocument("$cond",
                                            new BsonArray {
                                                new BsonDocument("$and",
                                                        new BsonArray {
                                                            new BsonDocument("$in",
                                                                    new BsonArray {
                                                                        "$action_code",
                                                                        new BsonArray {
                                                                            "Ocr",
                                                                            "DataConfirm",
                                                                            "DataConfirmBoolAuto",
                                                                            "DataEntry",
                                                                            "DataCheck",
                                                                            "Icr",
                                                                            "DataEntryBool",
                                                                            "DataConfirmAuto"
                                                                        }
                                                                    }),
                                                                new BsonDocument("$eq",
                                                                    new BsonArray {
                                                                        "$status",
                                                                        3
                                                                    }),
                                                                new BsonDocument("$or",
                                                                    new BsonArray {
                                                                        new BsonDocument("$and",
                                                                                new BsonArray {
                                                                                    new BsonDocument("$gt",
                                                                                            new BsonArray {
                                                                                                "$totalNL",
                                                                                                0
                                                                                            }),
                                                                                        new BsonDocument("$lt",
                                                                                            new BsonArray {
                                                                                                "$total",
                                                                                                "$totalNL"
                                                                                            })
                                                                                }),
                                                                            new BsonDocument("$and",
                                                                                new BsonArray {
                                                                                    new BsonDocument("$gt",
                                                                                            new BsonArray {
                                                                                                "$totalOCR",
                                                                                                0
                                                                                            }),
                                                                                        new BsonDocument("$lt",
                                                                                            new BsonArray {
                                                                                                "$total",
                                                                                                "$totalOCR"
                                                                                            })
                                                                                })
                                                                    })
                                                        }),
                                                    2,
                                                    "$status"
                                            })
                                })
                        }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            new BsonDocument {
                                {
                                    "action_code",
                                    "$action_code"
                                }, {
                                    "work_flow_step_instance_id",
                                    "$work_flow_step_instance_id"
                                }, {
                                    "doc_path",
                                    "$doc_path"
                                }, {
                                    "status",
                                    "$status"
                                }
                            }
                        }, {
                            "action_code",
                            new BsonDocument("$first", "$action_code")
                        }, {
                            "work_flow_step_instance_id",
                            new BsonDocument("$first", "$work_flow_step_instance_id")
                        }, {
                            "doc_path",
                            new BsonDocument("$first", "$doc_path")
                        }, {
                            "status",
                            new BsonDocument("$first", "$status")
                        }, {
                            "total",
                            new BsonDocument("$sum", 1)
                        }
                    })
            };
            var result = DbSet.Aggregate<BsonDocument>(bson).ToList();
            var response = result.Select(x => new TotalDocPathJob
            {
                ActionCode = x["action_code"].ToString(),
                WorkflowStepInstanceId = x["work_flow_step_instance_id"].AsGuid,
                Path = x["doc_path"].ToString(),
                Status = !string.IsNullOrEmpty(x["status"].ToString()) ? short.Parse(x["status"].ToString()) : (short)0,
                Total = !string.IsNullOrEmpty(x["total"].ToString()) ? int.Parse(x["total"].ToString()) : 0,
            }).ToList();

            return response;
        }

        public async Task<List<TotalDocPathJob>> GetSummaryDoc (FilterDefinition<Job> filter)
        {
            var serializerRegistry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
            var documentSerializer = serializerRegistry.GetSerializer<Job>();
            var f = filter.Render(documentSerializer, serializerRegistry).ToBsonDocument();
            var bson = new BsonDocument[]
            {
                new BsonDocument("$match",
                    f),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "doc_instance_id",
                            1
                        }, {
                            "doc_path",
                            1
                        }, {
                            "status",
                            1
                        }, {
                            "action_code",
                            1
                        }, {
                            "work_flow_step_instance_id",
                            1
                        }, {
                            "doc_type_field_sort_order",
                            1
                        },
                        { "batch_name", 1 },
                        { "num_of_round", 1 },
                        {
                            "isNL",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$action_code",
                                                "DataEntry"
                                            }),
                                        1,
                                        0
                                })
                        }, {
                            "isOCR",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$action_code",
                                                "OCR"
                                            }),
                                        1,
                                        0
                                })
                        }, {
                            "isDoubleSort",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$and",
                                            new BsonArray {
                                                new BsonDocument("$eq",
                                                        new BsonArray {
                                                            "$doc_type_field_sort_order",
                                                            1
                                                        }),
                                                    new BsonDocument("$eq",
                                                        new BsonArray {
                                                            "$action_code",
                                                            "DataEntry"
                                                        })
                                            }),
                                        1,
                                        0
                                })
                        }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            "$doc_instance_id"
                        }, {
                            "totalNL",
                            new BsonDocument("$sum", "$isNL")
                        }, {
                            "totalOCR",
                            new BsonDocument("$sum", "$isOCR")
                        }, {
                            "totalDoubleSort",
                            new BsonDocument("$sum", "$isDoubleSort")
                        }, {
                            "z",
                            new BsonDocument("$push",
                                new BsonDocument {
                                    {
                                        "doc_path",
                                        "$doc_path"
                                    }, {
                                        "status",
                                        "$status"
                                    }, {
                                        "action_code",
                                        "$action_code"
                                    }, {
                                        "work_flow_step_instance_id",
                                        "$work_flow_step_instance_id"
                                    },
                                    { "batch_name", "$batch_name" },
                                    { "num_of_round", "$num_of_round" }
                                })
                        }
                    }),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "doc_instance_id",
                            "$_id"
                        }, {
                            "totalNL",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$totalDoubleSort",
                                                0
                                            }),
                                        0,
                                        new BsonDocument("$divide",
                                            new BsonArray {
                                                "$totalNL",
                                                "$totalDoubleSort"
                                            })
                                })
                        }, {
                            "totalOCR",
                            1
                        }, {
                            "totalDoubleSort",
                            1
                        }, {
                            "z",
                            1
                        }
                    }),
                new BsonDocument("$unwind",
                    new BsonDocument("path", "$z")),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "doc_instance_id",
                            1
                        }, {
                            "totalNL",
                            1
                        }, {
                            "totalOCR",
                            1
                        }, {
                            "doc_path",
                            "$z.doc_path"
                        }, {
                            "action_code",
                            "$z.action_code"
                        }, {
                            "work_flow_step_instance_id",
                            "$z.work_flow_step_instance_id"
                        }, {
                            "status",
                            "$z.status"
                        },
                        { "batch_name", "$z.batch_name" },
                        { "num_of_round", "$z.num_of_round" }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            new BsonDocument {
                                {
                                    "action_code",
                                    "$action_code"
                                }, {
                                    "work_flow_step_instance_id",
                                    "$work_flow_step_instance_id"
                                }, {
                                    "doc_instance_id",
                                    "$doc_instance_id"
                                }, {
                                    "doc_path",
                                    "$doc_path"
                                }, {
                                    "status",
                                    "$status"
                                }
                            }
                        }, {
                            "totalNL",
                            new BsonDocument("$first", "$totalNL")
                        }, {
                            "totalOCR",
                            new BsonDocument("$first", "$totalOCR")
                        }, {
                            "action_code",
                            new BsonDocument("$first", "$action_code")
                        }, {
                            "work_flow_step_instance_id",
                            new BsonDocument("$first", "$work_flow_step_instance_id")
                        }, {
                            "doc_instance_id",
                            new BsonDocument("$first", "$doc_instance_id")
                        }, {
                            "doc_path",
                            new BsonDocument("$first", "$doc_path")
                        }, {
                            "status",
                            new BsonDocument("$first", "$status")
                        }, {
                            "total",
                            new BsonDocument("$sum", 1)
                        },
                        { "batch_name", new BsonDocument("$first", "$batch_name") },
                        { "num_of_round", new BsonDocument("$first", "$num_of_round") }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            new BsonDocument {
                                {
                                    "action_code",
                                    "$action_code"
                                }, {
                                    "work_flow_step_instance_id",
                                    "$work_flow_step_instance_id"
                                }, {
                                    "doc_instance_id",
                                    "$doc_instance_id"
                                }, {
                                    "doc_path",
                                    "$doc_path"
                                }
                            }
                        }, {
                            "totalNL",
                            new BsonDocument("$first", "$totalNL")
                        }, {
                            "totalOCR",
                            new BsonDocument("$first", "$totalOCR")
                        }, {
                            "action_code",
                            new BsonDocument("$first", "$action_code")
                        }, {
                            "work_flow_step_instance_id",
                            new BsonDocument("$first", "$work_flow_step_instance_id")
                        }, {
                            "doc_instance_id",
                            new BsonDocument("$first", "$doc_instance_id")
                        }, {
                            "doc_path",
                            new BsonDocument("$first", "$doc_path")
                        }, {
                            "status",
                            new BsonDocument("$first", "$status")
                        }, {
                            "total",
                            new BsonDocument("$first", "$total")
                        }, {
                            "totalSum",
                            new BsonDocument("$sum", 1)
                        },
                        { "batch_name", new BsonDocument("$first", "$batch_name") },
                        { "num_of_round", new BsonDocument("$first", "$num_of_round") }
                    }),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "action_code",
                            1
                        }, {
                            "work_flow_step_instance_id",
                            1
                        }, {
                            "doc_path",
                            1
                        }, {
                            "doc_instance_id",
                            1
                        }, {
                            "totalNL",
                            1
                        }, {
                            "totalOCR",
                            1
                        },
                        { "batch_name", 1 },
                        { "num_of_round", 1 },
                        {
                            "status",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$gt",
                                            new BsonArray {
                                                "$totalSum",
                                                1
                                            }),
                                        "2",
                                        new BsonDocument("$cond",
                                            new BsonArray {
                                                new BsonDocument("$and",
                                                        new BsonArray {
                                                            new BsonDocument("$in",
                                                                    new BsonArray {
                                                                        "$action_code",
                                                                        new BsonArray {
                                                                            "Ocr",
                                                                            "DataConfirm",
                                                                            "DataConfirmBoolAuto",
                                                                            "DataEntry",
                                                                            "DataCheck",
                                                                            "Icr",
                                                                            "DataEntryBool",
                                                                            "DataConfirmAuto"
                                                                        }
                                                                    }),
                                                                new BsonDocument("$eq",
                                                                    new BsonArray {
                                                                        "$status",
                                                                        3
                                                                    }),
                                                                new BsonDocument("$or",
                                                                    new BsonArray {
                                                                        new BsonDocument("$and",
                                                                                new BsonArray {
                                                                                    new BsonDocument("$gt",
                                                                                            new BsonArray {
                                                                                                "$totalNL",
                                                                                                0
                                                                                            }),
                                                                                        new BsonDocument("$lt",
                                                                                            new BsonArray {
                                                                                                "$total",
                                                                                                "$totalNL"
                                                                                            })
                                                                                }),
                                                                            new BsonDocument("$and",
                                                                                new BsonArray {
                                                                                    new BsonDocument("$gt",
                                                                                            new BsonArray {
                                                                                                "$totalOCR",
                                                                                                0
                                                                                            }),
                                                                                        new BsonDocument("$lt",
                                                                                            new BsonArray {
                                                                                                "$total",
                                                                                                "$totalOCR"
                                                                                            })
                                                                                })
                                                                    })
                                                        }),
                                                    2,
                                                    "$status"
                                            })
                                })
                        }
                    })
            };
            var result = DbSet.Aggregate<BsonDocument>(bson).ToList();

            var response = result.Select(x => new TotalDocPathJob
            {
                ActionCode = x["action_code"].AsString,
                WorkflowStepInstanceId = x["work_flow_step_instance_id"].AsGuid,
                Path = x["doc_path"].ToString(),
                Status = !string.IsNullOrEmpty(x["status"].ToString()) ? short.Parse(x["status"].ToString()) : (short)0,
                DocInstanceId = x["doc_instance_id"].AsGuid,
                BatchName = x.GetValue("batch_name", "").ToString(), // Lấy batch_name
                NumOfRound = !string.IsNullOrEmpty(x["num_of_round"].ToString()) ? int.Parse(x["num_of_round"].ToString()) : 0, // Lấy num_of_round
            }).ToList();

            return response;
        }

        public async Task<PagedListExtension<Job>> GetPagingExtensionAsync(FilterDefinition<Job> filter, SortDefinition<Job> sort = null, int index = 1, int size = 10)
        {
            var isNullFilter = false;
            IFindFluent<Job, Job> data;
            if (filter == null)
            {
                filter = Builders<Job>.Filter.Empty;
                isNullFilter = true;
            }

            var totalfilter = await DbSet.CountDocumentsAsync(filter);
            long totalCorrect = await DbSet.CountDocumentsAsync(filter & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.QACheckFinal)) & Builders<Job>.Filter.Eq(x => x.RightStatus, (int)EnumJob.RightStatus.Correct));
            var totalComplete = await DbSet.CountDocumentsAsync(filter & Builders<Job>.Filter.Eq(x => x.Status, (int)EnumJob.Status.Complete));
            var totalWrong = await DbSet.CountDocumentsAsync(filter & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.QACheckFinal)) & Builders<Job>.Filter.Eq(x => x.RightStatus, (int)EnumJob.RightStatus.Wrong));
            var totalIsIgnore = await DbSet.CountDocumentsAsync(filter & Builders<Job>.Filter.Ne(x => x.ActionCode, nameof(ActionCodeConstants.QACheckFinal)) & Builders<Job>.Filter.Eq(x => x.IsIgnore, true));
            var totalError = await DbSet.CountDocumentsAsync(filter & Builders<Job>.Filter.Eq(x => x.Status, (int)EnumJob.Status.Error));

            var total = isNullFilter ?
                totalfilter
                : await DbSet.EstimatedDocumentCountAsync();

            if (index == -1)
            {
                data = DbSet.Find(filter);
            }
            else
            {
                data = sort == null ?
                DbSet.Find(filter).Skip((index - 1) * size).Limit(size)
                : DbSet.Find(filter).Sort(sort).Skip((index - 1) * size).Limit(size);
            }

            return new PagedListExtension<Job>
            {
                Data = await data.ToListAsync(),
                PageIndex = index,
                PageSize = size,
                TotalCount = total,
                TotalCorrect = totalCorrect,
                TotalComplete = totalComplete,
                TotalWrong = totalWrong,
                TotalIsIgnore = totalIsIgnore,
                TotalFilter = (int)totalfilter,
                TotalError = totalError,
                TotalPages = (int)Math.Ceiling((decimal)totalfilter / size)
            };
        }

        public async Task<double> GetFalsePercentAsync(FilterDefinition<Job> filter)
        {
            var totalfilter = await DbSet.CountDocumentsAsync(filter);
            if (totalfilter == 0) return 0;

            var totalWrong = await DbSet.CountDocumentsAsync(filter &
                                                             Builders<Job>.Filter.Eq(x => x.Status,
                                                                 (short)EnumJob.Status.Complete) &
                                                             Builders<Job>.Filter.Eq(x => x.RightStatus,
                                                                 (int)EnumJob.RightStatus.Wrong));
            return Math.Round(totalWrong * 100.0 / totalfilter, 2);
        }

        public async Task<List<JobProcessingStatistics>> GetTotalJobProcessingStatistics_V2(FilterDefinition<Job> filter)
        {
            var serializerRegistry = MongoDB.Bson.Serialization.BsonSerializer.SerializerRegistry;
            var documentSerializer = serializerRegistry.GetSerializer<Job>();
            var f = filter.Render(documentSerializer, serializerRegistry).ToBsonDocument();
            var bson = new BsonDocument[]
            {
                new BsonDocument("$match",
                    f),
                new BsonDocument("$project",
                    new BsonDocument {
                        {
                            "user_instance_id",
                            1
                        }, {
                            "work_flow_step_instance_id",
                            1
                        }, {
                            "action_code",
                            1
                        }, {
                            "right_status",
                            1
                        }, {
                            "status",
                            1
                        }, {
                            "has_change",
                            1
                        }, {
                            "is_ignore",
                            1
                        }, {
                            "ignore",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$is_ignore",
                                                true
                                            }),
                                        1,
                                        0
                                })
                        }, {
                            "correct",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$has_change",
                                                false
                                            }),
                                        1,
                                        0
                                })
                        }, {
                            "wrong",
                            new BsonDocument("$cond",
                                new BsonArray {
                                    new BsonDocument("$eq",
                                            new BsonArray {
                                                "$has_change",
                                                true
                                            }),
                                        1,
                                        0
                                })
                        }
                    }),
                new BsonDocument("$group",
                    new BsonDocument {
                        {
                            "_id",
                            new BsonDocument {
                                {
                                    "user_instance_id",
                                    "$user_instance_id"
                                }, {
                                    "work_flow_step_instance_id",
                                    "$work_flow_step_instance_id"
                                }
                            }
                        }, {
                            "total_ignore",
                            new BsonDocument("$sum", "$ignore")
                        }, {
                            "total_correct",
                            new BsonDocument("$sum", "$correct")
                        }, {
                            "total_wrong",
                            new BsonDocument("$sum", "$wrong")
                        }, {
                            "user_instance_id",
                            new BsonDocument("$first", "$user_instance_id")
                        }, {
                            "work_flow_step_instance_id",
                            new BsonDocument("$first", "$work_flow_step_instance_id")
                        }, {
                            "action_code",
                            new BsonDocument("$first", "$action_code")
                        }, {
                            "total",
                            new BsonDocument("$sum", 1)
                        }
                    })
            };
            var result = DbSet.Aggregate<BsonDocument>(bson).ToList();

            var response = result.Select(x => new JobProcessingStatistics
            {
                UserInstanceId = x["user_instance_id"].AsGuid,
                WorkflowStepInstanceId = x["work_flow_step_instance_id"].AsGuid,
                ActionCode = x["action_code"].AsString,
                Total = !string.IsNullOrEmpty(x["total"].ToString()) ? long.Parse(x["total"].ToString()) : (long)0,
                Total_Correct = !string.IsNullOrEmpty(x["total_correct"].ToString()) ? long.Parse(x["total_correct"].ToString()) : (long)0,
                Total_Ignore = !string.IsNullOrEmpty(x["total_ignore"].ToString()) ? long.Parse(x["total_ignore"].ToString()) : (long)0,
                Total_Wrong = !string.IsNullOrEmpty(x["total_wrong"].ToString()) ? long.Parse(x["total_wrong"].ToString()) : (long)0
            }).ToList();

            return response;
        }

        public async Task<List<TotalJobProcessingStatistics>> GetTotalJobProcessingStatistics(FilterDefinition<Job> filter)
        {
            var aggregate = DbSet.Aggregate();
            aggregate = aggregate.Match(filter);
            var bsonProject = new BsonDocument
            {
                {"action_code", 1 },
                {"user_instance_id", 1 },
                {"right_status", 1 },
                {"status", 1 },
                {"has_change", 1 },
                {"is_ignore", 1 }
            };
            var bsonProject1 = new BsonDocument
            {
                { "user_instance_id", 1 },
                { "action_code", 1 },
                { "SegmentLabeling", new BsonDocument{ { "$cond", new BsonArray { new BsonDocument { { "$eq", new BsonArray { "$action_code", "SegmentLabeling" } } }, 1, 0 } } } },
                { "DataEntry_Process", new BsonDocument{ { "$cond", new BsonArray { new BsonDocument { { "$eq", new BsonArray { "$action_code", "DataEntry" } } }, 1, 0 } } } },

                {
                    "DataEntry_Process_isIgnore",
                    new BsonDocument("$cond",
                        new BsonDocument {
                            {
                                "if",
                                new BsonDocument("$and",
                                    new BsonArray {
                                        new BsonDocument("$eq",
                                                new BsonArray {
                                                    "$action_code",
                                                    "DataEntry"
                                                }),
                                            new BsonDocument("$eq",
                                                new BsonArray {
                                                    "$is_ignore",
                                                    true
                                                })
                                    })
                            }, {
                                "then",
                                1
                            }, {
                                "else",
                                0
                            }
                        })
                },
               {
                    "DataCheck_Correct",
                    new BsonDocument("$cond",
                        new BsonDocument {
                            {
                                "if",
                                new BsonDocument("$and",
                                    new BsonArray {
                                        new BsonDocument("$eq",
                                                new BsonArray {
                                                    "$action_code",
                                                    "DataCheck"
                                                }),
                                            new BsonDocument("$eq",
                                                new BsonArray {
                                                    "$has_change",
                                                    false
                                                })
                                    })
                            }, {
                                "then",
                                1
                            }, {
                                "else",
                                0
                            }
                        })
                },
                {
                    "DataCheck_Wrong",
                    new BsonDocument("$cond",
                        new BsonDocument {
                            {
                                "if",
                                new BsonDocument("$and",
                                    new BsonArray {
                                        new BsonDocument("$eq",
                                                new BsonArray {
                                                    "$action_code",
                                                    "DataCheck"
                                                }),
                                            new BsonDocument("$eq",
                                                new BsonArray {
                                                    "$has_change",
                                                    true
                                                })
                                    })
                            }, {
                                "then",
                                1
                            }, {
                                "else",
                                0
                            }
                        })
                },
                { "DataConfirm", new BsonDocument{ { "$cond", new BsonArray { new BsonDocument { { "$eq", new BsonArray { "$action_code", "DataConfirm" } } }, 1, 0 } } } },
                { "DataCheckFinal", new BsonDocument{ { "$cond", new BsonArray { new BsonDocument { { "$eq", new BsonArray { "$action_code", "CheckFinal" } } }, 1, 0 } } } },
                { "DataEntryBool", new BsonDocument{ { "$cond", new BsonArray { new BsonDocument { { "$eq", new BsonArray { "$action_code", "DataEntryBool" } } }, 1, 0 } } } },
            };

            var bsonProject2 = new BsonDocument
            {
                { "user_instance_id", 1},
                { "SegmentLabeling", 1 },
                { "DataEntry_Process", 1 },
                { "DataEntry_Process_isIgnore", 1 },
                { "DataEntry_Process_Input", new BsonDocument("$subtract",new BsonArray() { "$DataEntry_Process", "$DataEntry_Process_isIgnore"})},
                { "DataCheck_Correct", 1 },
                { "DataCheck_Wrong", 1 },
                { "DataCheck", new BsonDocument("$add",new BsonArray() { "$DataCheck_Correct", "$DataCheck_Wrong"})},
                { "DataConfirm",1 },
                { "DataCheckFinal", 1 },
                { "DataEntryBool", 1 }
            };

            var bsonProject3 = new BsonDocument
            {
                { "user_instance_id", 1},
                { "SegmentLabeling", 1 },
                { "DataEntry_Process", 1 },
                { "DataEntry_Process_isIgnore", 1 },
                { "DataEntry_Process_Input", 1},
                { "DataCheck_Correct", 1 },
                { "DataCheck_Wrong", 1 },
                { "DataCheck", 1 },
                { "DataConfirm", 1 },
                { "DataCheckFinal", 1 },
                { "DataEntryBool", 1 }
            };

            var bsonGroup = new BsonDocument
            {
                {"_id", new BsonDocument{ { "user_instance_id", "$user_instance_id" } } },
                {"user_instance_id", new BsonDocument{ { "$first", "$user_instance_id" } } },
                {"Total_SegmentLabeling", new BsonDocument{ { "$sum", "$SegmentLabeling" } } },
                {"Total_DataEntry_Process", new BsonDocument{ { "$sum", "$DataEntry_Process" } } },
                {"Total_DataEntry_Process_isIgnore", new BsonDocument{ { "$sum", "$DataEntry_Process_isIgnore" } } },
                {"Total_DataEntry_Process_Input", new BsonDocument{ { "$sum", "$DataEntry_Process_Input" } } },
                {"Total_DataCheck_Correct", new BsonDocument{ { "$sum", "$DataCheck_Correct" } } },
                {"Total_DataCheck_Wrong", new BsonDocument{ { "$sum", "$DataCheck_Wrong" } } },
                {"Total_DataCheck", new BsonDocument{ { "$sum", "$DataCheck" } } },

                {"Total_DataConfirm", new BsonDocument{ { "$sum", "$DataConfirm" } } },

                {"Total_DataCheckFinal", new BsonDocument{ { "$sum", "$DataCheckFinal" } } },
                {"Total_DataEntryBool", new BsonDocument{ { "$sum", "$DataEntryBool" } } }
            };


            var a = aggregate.Project(bsonProject).Project(bsonProject1).Project(bsonProject2).Group(bsonGroup);
            var response = aggregate.Project(bsonProject).Project(bsonProject1).Project(bsonProject2).Project(bsonProject3).Group(bsonGroup).ToList().Select(x => new TotalJobProcessingStatistics
            {
                UserInstanceId = x["user_instance_id"].AsGuid,
                SegmentLabeling = x["Total_SegmentLabeling"].ToString(),
                DataEntryProcess = x["Total_DataEntry_Process"].ToString(),
                DataEntryProcessIsIgnore = x["Total_DataEntry_Process_isIgnore"].ToString(),
                DataEntryProcessInput = x["Total_DataEntry_Process_Input"].ToString(),

                DataCheckCorrect = x["Total_DataCheck_Correct"].ToString(),
                DataCheckWrong = x["Total_DataCheck_Wrong"].ToString(),
                DataCheck = x["Total_DataCheck"].ToString(),

                DataConfirm = x["Total_DataConfirm"].ToString(),

                DataCheckFinal = x["Total_DataCheckFinal"].ToString(),
                DataEntryBool = x["Total_DataEntryBool"].ToString()
            }).ToList();

            return response;
        }

        public async Task<List<TotalJobProcessingStatistics>> TotalJobPaymentStatistics(FilterDefinition<Job> filter)
        {
            //var aggregate = DbSet.Aggregate();
            //aggregate = aggregate.Match(filter);
            //var lq = await aggregate.Group(x => new { x.ActionCode, x.Status, x.RightStatus },
            //    g => new
            //    {
            //        g.First().ActionCode,
            //        g.First().Status,
            //        g.First().RightStatus
            //    }
            //    ).Group(z => new { z.ActionCode, z.Status, z.RightStatus },
            //    l => new
            //    {
            //        l.First().ActionCode,
            //        l.First().Status,
            //        l.First().RightStatus,
            //        Total = l.Count()
            //    }).SortBy(x => x.ActionCode).ToListAsync();
            //var se = lq.Select(i => new TotalJobProcessingStatistics
            //{
            //    ActionCode = i.ActionCode,
            //    Total = i.Total,
            //    Status = i.Status,
            //}).ToList();

            return null;
        }

        public async Task<List<ProjectCountExtension>> GetCountJobInProject(FilterDefinition<Job> filter)
        {
            var aggregate = DbSet.Aggregate();
            aggregate = aggregate.Match(filter);
            return await aggregate.Group(
                i => new { i.ProjectInstanceId, i.ActionCode, i.WorkflowInstanceId, i.InputType, i.DocTypeFieldInstanceId },
                g => new ProjectCountExtension
                {
                    ProjectInstanceId = g.First().ProjectInstanceId,
                    ActionCode = g.First().ActionCode,
                    WorkflowInstanceId = g.First().WorkflowInstanceId,
                    TotalJob = g.Count(),
                    InputType = g.First().InputType,
                    DocTypeFieldInstanceId = g.First().DocTypeFieldInstanceId
                })
            .ToListAsync();

        }

        public async Task<List<CountJobEntity>> GetCountAllJobByStatus()
        {
            var sw = new Stopwatch();
            sw.Start();
            var aggregate = DbSet.Aggregate();
            var result = await aggregate
                .SortBy(x=>x.Status) // and need to create index by status for best performance
                .Group(x => x.Status,
                l => new CountJobEntity
                {
                    Status = (EnumJob.Status)l.First().Status,
                    Total = l.Count()
                })
                .ToListAsync();
            sw.Stop();
            Log.Debug($"Done GetCountAllJobByStatus in {sw.ElapsedMilliseconds} ms");
            return result;
        }

        public async Task<List<CountJobEntity>> GetSummaryJobByAction(Guid projectInstanceId, string fromDate, string toDate)
        {

            var aggregateTotal = DbSet.Aggregate();
            var lstTotal = await aggregateTotal
                .Match(x => x.ProjectInstanceId == projectInstanceId)
                .Group(x => x.ActionCode,
                l => new CountJobEntity
                {
                    ActionCode = l.First().ActionCode,
                    Total = l.Count()
                })
                .ToListAsync();

            var aggregate = DbSet.Aggregate();
            if (!string.IsNullOrEmpty(fromDate))
            {
                DateTime fDate = DateTime.ParseExact(fromDate, "yyyyMMdd", null);
                aggregate = aggregate.Match(x => x.LastModificationDate >= fDate);
            }
            if (!string.IsNullOrEmpty(toDate))
            {
                DateTime tDate = DateTime.ParseExact(toDate, "yyyyMMdd", null);
                aggregate = aggregate.Match(x => x.LastModificationDate < tDate);
            }
            var lstDone = await aggregate
                .Match(x => x.ProjectInstanceId == projectInstanceId && x.Status == (short)EnumJob.Status.Complete)
                .Group(x => x.ActionCode,
                l => new CountJobEntity
                {
                    ActionCode = l.First().ActionCode,
                    Complete = l.Count()
                })
                .ToListAsync();

            var result = from t in lstTotal
                         join d in lstDone on t.ActionCode equals d.ActionCode into gj
                         from gr in gj.DefaultIfEmpty()
                         select new CountJobEntity
                         {
                             ActionCode = t.ActionCode,
                             Total = t.Total,
                             Complete = gr?.Complete ?? 0
                         };

            return result.ToList();
        }

        public async Task<List<CountJobEntity>> GetSummaryDocByAction(Guid projectInstanceId, List<WorkflowStepInfo> wfsInfoes,
            List<WorkflowSchemaConditionInfo> wfSchemaInfoes, string fromDate, string toDate)
        {
            #region Count total Doc
            var aggregateTotal = DbSet.Aggregate();
            var lstTotal = await aggregateTotal
                .Match(x => x.ProjectInstanceId == projectInstanceId && x.Status != (short)EnumJob.Status.Ignore)
                .Group(x => new { x.ActionCode, x.DocInstanceId, x.WorkflowStepInstanceId },
                l => new CountJobEntity
                {
                    ActionCode = l.First().ActionCode,
                    DocInstanceId = l.First().DocInstanceId,
                    WorkflowStepInstanceId = l.First().WorkflowStepInstanceId,
                    Total = l.Count()
                })
                .ToListAsync();

            var lstCountTotal = lstTotal.GroupBy(x => new { x.ActionCode, x.WorkflowStepInstanceId })
                .Select(x => new CountJobEntity
                {
                    ActionCode = x.Key.ActionCode,
                    WorkflowStepInstanceId = x.Key.WorkflowStepInstanceId,
                    Total = x.Count()
                }).ToList();
            #endregion

            #region Count Complete Doc
            var aggregate = DbSet.Aggregate();
            if (!string.IsNullOrEmpty(fromDate))
            {
                DateTime fDate = DateTime.ParseExact(fromDate, "yyyyMMdd", null);
                aggregate = aggregate.Match(x => x.CreatedDate >= fDate);
            }
            if (!string.IsNullOrEmpty(toDate))
            {
                DateTime tDate = DateTime.ParseExact(toDate, "yyyyMMdd", null);
                aggregate = aggregate.Match(x => x.CreatedDate < tDate);
            }
            //var lstJobDone = await aggregate
            //    .Match(x => x.ProjectInstanceId == projectInstanceId && x.Status == (short)EnumJob.Status.Complete)
            //    .Group(x => new { x.ActionCode, x.DocInstanceId, x.WorkflowStepInstanceId },
            //    l => new
            //    {
            //        ActionCode = l.First().ActionCode,
            //        DocInstanceId = l.First().DocInstanceId,
            //        WorkflowStepInstanceId = l.First().WorkflowStepInstanceId,
            //        Complete = l.Count(),
            //    })
            //    .ToListAsync();
            #endregion

            #region Old code
            //#region Count UnComplete Doc
            //var lstNotDone = await aggregate
            //    .Match(x => x.ProjectInstanceId == projectInstanceId && x.Status != (short)EnumJob.Status.Complete)
            //    .Group(x => new { x.ActionCode, x.DocInstanceId, x.WorkflowStepInstanceId },
            //    l => new
            //    {
            //        ActionCode = l.First().ActionCode,
            //        DocInstanceId = l.First().DocInstanceId,
            //        WorkflowStepInstanceId = l.First().WorkflowStepInstanceId,
            //        Total = l.Count()
            //    })
            //    .ToListAsync();
            //#endregion

            ////Remove doc in Complete list if exit job in UnComplete list
            //foreach (var item in lstDone)
            //{
            //    var uDone = lstNotDone.Where(x => x.DocInstanceId == item.DocInstanceId &&
            //                                      x.ActionCode == item.ActionCode &&
            //                                      x.WorkflowStepInstanceId == item.WorkflowStepInstanceId).FirstOrDefault();
            //    if(uDone == null)
            //    {
            //        var uTotal = lstTotal.Where(x => x.DocInstanceId == item.DocInstanceId &&
            //                                      x.ActionCode == item.ActionCode &&
            //                                      x.WorkflowStepInstanceId == item.WorkflowStepInstanceId).FirstOrDefault();
            //    }
            //}

            ////Summary complete Doc
            //var lstCountDone = lstDone.GroupBy(x => new { x.ActionCode, x.WorkflowStepInstanceId })
            //    .Select(x => new CountJobEntity
            //    {
            //        ActionCode = x.Key.ActionCode,
            //        WorkflowStepInstanceId = x.Key.WorkflowStepInstanceId,
            //        Complete = x.Count()
            //    }).ToList();

            //var result = (from t in lstCountTotal
            //              join d in lstCountDone on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
            //              from gr in gj.DefaultIfEmpty()
            //              select new CountJobEntity
            //              {
            //                  ActionCode = t.ActionCode,
            //                  WorkflowStepInstanceId = t.WorkflowStepInstanceId,
            //                  Total = t.Total,
            //                  Complete = gr?.Complete ?? 0
            //              }).ToList();
            #endregion

            //var lst = (from t in lstTotal
            //           join d in lstJobDone on new { t.ActionCode, t.WorkflowStepInstanceId, t.DocInstanceId }
            //                                    equals new { d.ActionCode, d.WorkflowStepInstanceId, d.DocInstanceId } into gj
            //           from gr in gj.DefaultIfEmpty()
            //           select new
            //           {
            //               ActionCode = t.ActionCode,
            //               WorkflowStepInstanceId = t.WorkflowStepInstanceId,
            //               DocInstanceId = t.DocInstanceId,
            //               Total = t.Total,
            //               Complete = gr?.Complete ?? 0
            //           }).ToList();

            #region Check xem file có còn job chưa hoàn thành ở các bước trước hay không
            //Get list step 
            List<WfStep> lstStep = aggregate
                .Match(x => x.ProjectInstanceId == projectInstanceId)
                .Group(x => x.WorkflowStepInstanceId,
                l => new WfStep
                {
                    WorkflowStepInstanceId = l.First().WorkflowStepInstanceId,
                    ActionCode = l.First().ActionCode
                })
                .ToList();

            //Create list All before step off the step
            List<WfStepOrder> lstWfStepOrder = new List<WfStepOrder>();
            foreach (var step in lstStep)
            {
                var beforeWfsInfoIncludeCurrentStep = WorkflowHelper.GetAllBeforeSteps(wfsInfoes, wfSchemaInfoes, step.WorkflowStepInstanceId.GetValueOrDefault(), true);
                WfStepOrder obj = new WfStepOrder();
                obj.WorkflowStepInstanceId = step.WorkflowStepInstanceId;
                obj.wfStepBefore = beforeWfsInfoIncludeCurrentStep;
                lstWfStepOrder.Add(obj);
            }

            //Get list file not done by Step
            #region Count UnComplete Doc
            var lstNotDone = await aggregate
                .Match(x => x.ProjectInstanceId == projectInstanceId &&
                            x.Status != (short)EnumJob.Status.Ignore &&
                            x.Status != (short)EnumJob.Status.Complete)
                .Group(x => new { x.ActionCode, x.DocInstanceId, x.WorkflowStepInstanceId },
                l => new
                {
                    ActionCode = l.First().ActionCode,
                    DocInstanceId = l.First().DocInstanceId,
                    WorkflowStepInstanceId = l.First().WorkflowStepInstanceId,
                    Total = l.Count()
                })
                .ToListAsync();

            List<DocByWfStep> lstDocNotDoneByWfStep = new List<DocByWfStep>();
            foreach (var step in lstStep)
            {
                List<Guid?> docs = lstNotDone.Where(x => x.WorkflowStepInstanceId == step.WorkflowStepInstanceId).Select(x => x.DocInstanceId).ToList();
                DocByWfStep obj = new DocByWfStep();
                obj.WorkflowStepInstanceId = step.WorkflowStepInstanceId;
                obj.DocInstanceIds = docs;
                lstDocNotDoneByWfStep.Add(obj);
            }

            #endregion

            List<CountJobEntity> lstDocDone = new List<CountJobEntity>();
            foreach (var fileItem in lstTotal)
            {
                //Get list step for fileItem
                WfStepOrder stepOder = lstWfStepOrder.Where(x => x.WorkflowStepInstanceId == fileItem.WorkflowStepInstanceId).FirstOrDefault();
                bool hasJobNotDone = false;
                if (stepOder != null)
                {

                    if (stepOder.wfStepBefore.Any())
                    {
                        foreach (var step in stepOder.wfStepBefore)
                        {
                            var obj = lstDocNotDoneByWfStep.Where(x => x.WorkflowStepInstanceId == step.InstanceId).FirstOrDefault();
                            if (obj != null && obj.DocInstanceIds.Any())
                            {
                                if (obj.DocInstanceIds.Contains(fileItem.DocInstanceId))
                                {
                                    hasJobNotDone = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (!hasJobNotDone)
                {
                    lstDocDone.Add(fileItem);
                }
            }

            #endregion

            var lstCountDone = lstDocDone
                .GroupBy(g => new { g.ActionCode, g.WorkflowStepInstanceId })
                          .Select(t => new CountJobEntity
                          {
                              ActionCode = t.Key.ActionCode,
                              WorkflowStepInstanceId = t.Key.WorkflowStepInstanceId,
                              Complete = t.Count()
                          }).ToList();

            var result = (from t in lstCountTotal
                          join d in lstCountDone on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
                          from gr in gj.DefaultIfEmpty()
                          select new CountJobEntity
                          {
                              ActionCode = t.ActionCode,
                              WorkflowStepInstanceId = t.WorkflowStepInstanceId,
                              Total = t.Total,
                              Complete = gr?.Complete ?? 0
                          }).ToList();

            return result;
        }


        public async Task<WorkSpeedReportEntity> GetWorkSpeed(Guid? projectInstanceId, Guid? userInstanceId)
        {
            var aggregate = DbSet.Aggregate();
            if (projectInstanceId.HasValue)
            {
                aggregate = aggregate.Match(x => x.ProjectInstanceId == projectInstanceId);
            }
            if (userInstanceId.HasValue)
            {
                aggregate = aggregate.Match(x => x.UserInstanceId == userInstanceId);
            }

            aggregate = aggregate.Match(x => x.Status == (short)EnumJob.Status.Complete);

            var lst1 = await aggregate
                .SortByDescending(x => x.LastModificationDate)
                .Group(x => new { x.UserInstanceId, x.ActionCode, x.TurnInstanceId },
                l => new
                {
                    UserInstanceId = l.First().UserInstanceId,
                    ActionCode = l.First().ActionCode,
                    TurnInstanceId = l.First().TurnInstanceId,
                    ReceivedDate = l.First().ReceivedDate,
                    LastModificationDate = l.First().LastModificationDate,
                    TotalJob = l.Count()
                }
                ).ToListAsync();

            var lst2 = lst1.Select(x => new
            {
                UserInstanceId = x.UserInstanceId,
                ActionCode = x.ActionCode,
                TurnInstanceId = x.TurnInstanceId,
                WorkTimeOfTurn = (x.LastModificationDate - x.ReceivedDate).Value.Seconds,
                TotalJob = x.TotalJob
            }
            ).ToList();

            var lst3 = lst2.GroupBy(g => new { g.UserInstanceId, g.ActionCode })
                .Select(x => new
                {
                    UserInstanceId = x.Key.UserInstanceId,
                    ActionCode = x.Key.ActionCode,
                    WorkTime = x.Sum(x => x.WorkTimeOfTurn),
                    TotalJob = x.Sum(x => x.TotalJob)
                }).ToList();

            List<WorkSpeedDetailEntity> lstDetail = lst3.Select(x => new WorkSpeedDetailEntity
            {
                UserInstanceId = x.UserInstanceId,
                ActionCode = x.ActionCode,
                TotalJob = x.TotalJob,
                TotalTime = x.WorkTime,
                WorkSpeed = x.WorkTime == 0 ? 0 : x.TotalJob * 60 / x.WorkTime
            }).ToList();

            #region Tong hop toc do theo Action
            List<WorkSpeedTotalEntity> lstWsTotal = lstDetail.GroupBy(g => g.ActionCode)
                .Select(x => new WorkSpeedTotalEntity
                {
                    ActionCode = x.Key,
                    TotalJob = x.Sum(s => s.TotalJob),
                    TotalTime = x.Sum(s => s.TotalTime),
                    WorkSpeed = x.Sum(s => s.TotalTime) == 0 ? 0 : x.Sum(s => s.TotalJob) * 60 / x.Sum(s => s.TotalTime)
                }).ToList();

            #endregion

            WorkSpeedReportEntity result = new WorkSpeedReportEntity();
            result.WorkSpeedTotal = lstWsTotal;
            result.WorkSpeedDetail = lstDetail;

            return result;
        }


        public async Task<List<JobByDocDoneEntity>> GetSummaryJobOfDoneFileByStep(Guid? projectInstanceId, string lastAction)
        {
            var aggregate = DbSet.Aggregate();
            if (projectInstanceId.HasValue)
            {
                aggregate = aggregate.Match(x => x.ProjectInstanceId == projectInstanceId);
            }
            //aggregate = aggregate.Match(x => x.Status == (short)EnumJob.Status.Complete);

            //Get list complete file
            var lstDoneDoc = await aggregate
                .Match(x => x.ActionCode == lastAction && x.Status == (short)EnumJob.Status.Complete)
                .Group(x => new { x.DocInstanceId },
                l => new DocItemEntity
                {
                    DocInstanceId = l.First().DocInstanceId,
                    DocName = l.First().DocName
                }
                ).ToListAsync();

            //Count job by file and step
            var lst1 = await aggregate
                .Group(x => new { x.WorkflowStepInstanceId, x.ActionCode, x.DocInstanceId },
                l => new
                {
                    WorkflowStepInstanceId = l.First().WorkflowStepInstanceId,
                    ActionCode = l.First().ActionCode,
                    DocInstanceId = l.First().DocInstanceId,
                    TotalJob = l.Count()
                }
                ).ToListAsync();

            var lst2 = (from t1 in lst1
                        join t2 in lstDoneDoc on t1.DocInstanceId equals t2.DocInstanceId
                        select t1).ToList();

            var totalJob = lst2.Select(x => x.TotalJob).Sum();

            var result = lst2.GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                .Select(x => new JobByDocDoneEntity
                {
                    WorkflowStepInstanceId = x.First().WorkflowStepInstanceId,
                    ActionCode = x.First().ActionCode,
                    JobCount = x.Sum(s => s.TotalJob),
                    TotalJob = totalJob,
                    Percent = totalJob == 0 ? 0 : (int)Math.Round((decimal)x.Sum(s => s.TotalJob) * 100 / (Decimal)totalJob, MidpointRounding.ToEven)
                }).ToList();

            return result;
        }

        public async Task<List<JobOfFileEntity>> GetSummaryJobOfFile(Guid? docInstanceId)
        {
            var aggregate = DbSet.Aggregate();
            if (docInstanceId.HasValue)
            {
                aggregate = aggregate.Match(x => x.DocInstanceId == docInstanceId);
            }

            var lstJob = await aggregate.ToListAsync();

            var lstTotalJob = lstJob.GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                            .Select(l => new JobOfFileEntity
                            {
                                WorkflowStepInstanceId = l.Key.WorkflowStepInstanceId,
                                ActionCode = l.Key.ActionCode,
                                Unit = l.First().DocTypeFieldInstanceId == null ? "File" : "Meta",
                                TotalJob = l.Count()
                            }).ToList();

            var lstWaitJob = lstJob.Where(x => x.Status == (short)EnumJob.Status.Waiting).GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                            .Select(l => new JobOfFileEntity
                            {
                                WorkflowStepInstanceId = l.Key.WorkflowStepInstanceId,
                                ActionCode = l.Key.ActionCode,
                                WaitingJob = l.Count()
                            }).ToList();

            var lstProcessJob = lstJob.Where(x => x.Status == (short)EnumJob.Status.Processing).GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                            .Select(l => new JobOfFileEntity
                            {
                                WorkflowStepInstanceId = l.Key.WorkflowStepInstanceId,
                                ActionCode = l.Key.ActionCode,
                                ProcessingJob = l.Count()
                            }).ToList();

            var lstErrorJob = lstJob.Where(x => x.Status == (short)EnumJob.Status.Error).GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                            .Select(l => new JobOfFileEntity
                            {
                                WorkflowStepInstanceId = l.Key.WorkflowStepInstanceId,
                                ActionCode = l.Key.ActionCode,
                                ErrorJob = l.Count()
                            }).ToList();

            var lstDoneJob = lstJob.Where(x => x.Status == (short)EnumJob.Status.Complete).GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                            .Select(l => new JobOfFileEntity
                            {
                                WorkflowStepInstanceId = l.Key.WorkflowStepInstanceId,
                                ActionCode = l.Key.ActionCode,
                                CompleteJob = l.Count()
                            }).ToList();

            var lstIgnoreJob = lstJob.Where(x => x.Status == (short)EnumJob.Status.Ignore).GroupBy(x => new { x.WorkflowStepInstanceId, x.ActionCode })
                            .Select(l => new JobOfFileEntity
                            {
                                WorkflowStepInstanceId = l.Key.WorkflowStepInstanceId,
                                ActionCode = l.Key.ActionCode,
                                IgnoreJob = l.Count()
                            }).ToList();

            var result = (from t in lstTotalJob
                          join d in lstWaitJob on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
                          from gr in gj.DefaultIfEmpty()
                          select new JobOfFileEntity
                          {
                              WorkflowStepInstanceId = t.WorkflowStepInstanceId,
                              ActionCode = t.ActionCode,
                              Unit = t.Unit,
                              TotalJob = t.TotalJob,
                              WaitingJob = gr != null ? gr.WaitingJob : 0
                          }).ToList();

            result = (from t in result
                      join d in lstProcessJob on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
                      from gr in gj.DefaultIfEmpty()
                      select new JobOfFileEntity
                      {
                          WorkflowStepInstanceId = t.WorkflowStepInstanceId,
                          ActionCode = t.ActionCode,
                          Unit = t.Unit,
                          TotalJob = t.TotalJob,
                          WaitingJob = t.WaitingJob,
                          ProcessingJob = gr != null ? gr.ProcessingJob : 0
                      }).ToList();

            result = (from t in result
                      join d in lstErrorJob on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
                      from gr in gj.DefaultIfEmpty()
                      select new JobOfFileEntity
                      {
                          WorkflowStepInstanceId = t.WorkflowStepInstanceId,
                          ActionCode = t.ActionCode,
                          Unit = t.Unit,
                          TotalJob = t.TotalJob,
                          WaitingJob = t.WaitingJob,
                          ProcessingJob = t.ProcessingJob,
                          ErrorJob = gr != null ? gr.ErrorJob : 0
                      }).ToList();

            result = (from t in result
                      join d in lstDoneJob on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
                      from gr in gj.DefaultIfEmpty()
                      select new JobOfFileEntity
                      {
                          WorkflowStepInstanceId = t.WorkflowStepInstanceId,
                          ActionCode = t.ActionCode,
                          Unit = t.Unit,
                          TotalJob = t.TotalJob,
                          WaitingJob = t.WaitingJob,
                          ProcessingJob = t.ProcessingJob,
                          ErrorJob = t.ErrorJob,
                          CompleteJob = gr != null ? gr.CompleteJob : 0
                      }).ToList();

            result = (from t in result
                      join d in lstIgnoreJob on new { t.ActionCode, t.WorkflowStepInstanceId } equals new { d.ActionCode, d.WorkflowStepInstanceId } into gj
                      from gr in gj.DefaultIfEmpty()
                      select new JobOfFileEntity
                      {
                          WorkflowStepInstanceId = t.WorkflowStepInstanceId,
                          ActionCode = t.ActionCode,
                          Unit = t.Unit,
                          TotalJob = t.TotalJob,
                          WaitingJob = t.WaitingJob,
                          ProcessingJob = t.ProcessingJob,
                          ErrorJob = t.ErrorJob,
                          CompleteJob = t.CompleteJob,
                          IgnoreJob = gr != null ? gr.IgnoreJob : 0
                      }).ToList();
            return result;
        }

        public async Task<Job> UpdateAndLockRecordAsync(Job entity)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.Id, entity.Id) & Builders<Job>.Filter.Eq(x => x.Status, (short)EnumJob.Status.Waiting);
            var record = await DbSet.FindOneAndUpdateAsync(filter,
              Builders<Job>.Update.Set(j => j.UserInstanceId, entity.UserInstanceId)
              .Set(j => j.TurnInstanceId, entity.TurnInstanceId)
              .Set(j => j.ReceivedDate, entity.ReceivedDate)
              .Set(j => j.DueDate, entity.DueDate)
              .Set(j => j.Status, entity.Status)
              .Set(j => j.LastModificationDate, entity.LastModificationDate)
              .Set(j => j.Value, entity.Value)
              .Set(j => j.OldValue, entity.OldValue),

              options: new FindOneAndUpdateOptions<Job>
              {
                  ReturnDocument = ReturnDocument.After
              });
            return record;
        }

        public async Task<Job> GetJobByInstanceId(Guid instanceId)
        {
            var filter = Builders<Job>.Filter.Eq(x => x.InstanceId, instanceId);
            var job = DbSet.Find(filter);
            return await job.SingleOrDefaultAsync();
        }
        public async Task<List<Job>> GetJobsByInstanceIds(List<Guid> instanceIds)
        {
            var filter = Builders<Job>.Filter.In(x => x.InstanceId, instanceIds);
            var jobs = DbSet.Find(filter);
            return await jobs.ToListAsync();
        }
    }
}
