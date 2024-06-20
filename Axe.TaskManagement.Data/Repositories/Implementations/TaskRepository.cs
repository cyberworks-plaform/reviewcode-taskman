using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class TaskRepository : MongoBaseRepository<TaskEntity>, ITaskRepository
    {
        public TaskRepository(IMongoContext context) : base(context)
        {
        }

        public async Task<bool> ChangeStatus(string id, short newStatus = (short)EnumTask.Status.Processing)
        {
            var objectId = new ObjectId(id);
            var filter = Builders<TaskEntity>.Filter.Eq(x => x.Id, objectId);
            var updateStatus = Builders<TaskEntity>.Update.Set(s => s.Status, newStatus);
            return await UpdateOneAsync(filter, updateStatus);
        }

        public async Task<bool> ChangeStatusExternal(Guid instanceId, short newStatus = (short)EnumTask.Status.Processing)
        {
            var filter = Builders<TaskEntity>.Filter.Eq(x => x.InstanceId, instanceId);
            var updateStatus = Builders<TaskEntity>.Update.Set(s => s.Status, newStatus);
            return await UpdateOneAsync(filter, updateStatus);
        }

        public async Task<bool> ChangeStatusMulti(List<string> ids, short newStatus = (short)EnumTask.Status.Processing)
        {
            var objectIds = ids.Select(x => new ObjectId(x));
            var filter = Builders<TaskEntity>.Filter.In(nameof(TaskEntity.Id), objectIds);
            var updateStatus = Builders<TaskEntity>.Update.Set(s => s.Status, newStatus);
            return await UpdateOneAsync(filter, updateStatus);
        }

        public async Task<TaskEntity> UpdateProgressValue(string id, TaskStepProgress changeTaskStepProgress, short? newStatus = null)
        {
            var objectId = new ObjectId(id);
            // Cheating: avoid multiple update in a document at the same time
            Random rnd = new Random();
            int delayUpdateTask = rnd.Next(200, 1000);
            await Task.Delay(delayUpdateTask);
            var task = await GetByIdAsync(objectId);

            if (task == null)
            {
                return null;
            }

            var taskStepProgresses = new List<TaskStepProgress>();
            if (string.IsNullOrEmpty(task.Progress))
            {
                taskStepProgresses.Add(changeTaskStepProgress);
            }
            else
            {
                taskStepProgresses = JsonConvert.DeserializeObject<List<TaskStepProgress>>(task.Progress);
                var crrTaskStepProgress =
                    taskStepProgresses.FirstOrDefault(x => x.InstanceId == changeTaskStepProgress.InstanceId);
                if (crrTaskStepProgress == null)
                {
                    taskStepProgresses.Add(changeTaskStepProgress);
                }
                else
                {
                    crrTaskStepProgress.WaitingJob += changeTaskStepProgress.WaitingJob;
                    crrTaskStepProgress.ProcessingJob += changeTaskStepProgress.ProcessingJob;
                    crrTaskStepProgress.CompleteJob += changeTaskStepProgress.CompleteJob;
                    crrTaskStepProgress.TotalJob += changeTaskStepProgress.TotalJob;
                    crrTaskStepProgress.Status = changeTaskStepProgress.Status;
                    if (crrTaskStepProgress.WaitingJob < 0)
                    {
                        crrTaskStepProgress.WaitingJob = 0;
                    }
                    if (crrTaskStepProgress.ProcessingJob < 0)
                    {
                        crrTaskStepProgress.ProcessingJob = 0;
                    }
                    if (crrTaskStepProgress.CompleteJob < 0)
                    {
                        crrTaskStepProgress.CompleteJob = 0;
                    }
                    if (crrTaskStepProgress.TotalJob < 0)
                    {
                        crrTaskStepProgress.TotalJob = 0;
                    }
                }
            }

            task.Progress = JsonConvert.SerializeObject(taskStepProgresses);

            if (newStatus != null)
            {
                task.Status = newStatus.Value;
            }

            return await UpdateAsync(task);
        }

        public async Task<bool> DeleteByDocInstanceIdAsync(Guid docInstanceId)
        {
            var result = await DbSet.DeleteOneAsync(Builders<TaskEntity>.Filter.Eq(nameof(TaskEntity.DocInstanceId), docInstanceId));
            return result.IsAcknowledged;
        }

        public async Task<TaskEntity> GetByDocInstanceId(Guid docInstanceId)
        {
            var result = new TaskEntity();
            var filter = Builders<TaskEntity>.Filter.Eq("doc_instance_id", docInstanceId);
            result = await DbSet.Aggregate().Match(filter).FirstOrDefaultAsync();

            return result;
        }
    }
}
