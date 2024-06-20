using System;
using System.Collections.Generic;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using MongoDB.Driver;
using System.Threading.Tasks;
using MongoDB.Bson;

namespace Axe.TaskManagement.Data.Repositories.Implementations
{
    public class QueueLockRepository : MongoBaseRepository<QueueLock>, IQueueLockRepository
    {
        public QueueLockRepository(IMongoContext context) : base(context)
        {
        }

        public async Task<List<QueueLock>> UpSertMultiQueueLockAsync(IEnumerable<QueueLock> entities)
        {
            var result = new List<QueueLock>();
            var updateOneModels = new List<UpdateOneModel<QueueLock>>();
            foreach (var entity in entities)
            {
                result.Add(entity);

                var filter1 = Builders<QueueLock>.Filter.Eq(x => x.DocInstanceId, entity.DocInstanceId);
                var filter2 = Builders<QueueLock>.Filter.Eq(x => x.WorkflowStepInstanceId, entity.WorkflowStepInstanceId);
                var filter3 = Builders<QueueLock>.Filter.Eq(x => x.DocTypeFieldInstanceId, entity.DocTypeFieldInstanceId);
                var filter4 = Builders<QueueLock>.Filter.Eq(x => x.DocFieldValueInstanceId, entity.DocFieldValueInstanceId);
                var filter = filter1 & filter2 & filter3 & filter4;

                var updateValue = Builders<QueueLock>.Update
                    //.Set(s => s.Id, entity.Id)
                    .Set(s => s.FileInstanceId, entity.FileInstanceId)
                    .Set(s => s.DocInstanceId, entity.DocInstanceId)
                    .Set(s => s.DocName, entity.DocName)
                    .Set(s => s.DocCreatedDate, entity.DocCreatedDate)
                    .Set(s => s.DocPath, entity.DocPath)
                    .Set(s => s.DigitizedTemplateInstanceId, entity.DigitizedTemplateInstanceId)
                    .Set(s => s.DigitizedTemplateCode, entity.DigitizedTemplateCode)
                    .Set(s => s.DocTypeFieldInstanceId, entity.DocTypeFieldInstanceId)
                    .Set(s => s.DocTypeFieldSortOrder, entity.DocTypeFieldSortOrder)
                    .Set(s => s.DocFieldValueInstanceId, entity.DocFieldValueInstanceId)
                    .Set(s => s.ProjectTypeInstanceId, entity.ProjectTypeInstanceId)
                    .Set(s => s.ProjectInstanceId, entity.ProjectInstanceId)
                    .Set(s => s.WorkflowInstanceId, entity.WorkflowInstanceId)
                    .Set(s => s.WorkflowStepInstanceId, entity.WorkflowStepInstanceId)
                    .Set(s => s.ActionCode, entity.ActionCode)
                    .Set(s => s.InputParam, entity.InputParam)
                    .Set(s => s.Status, entity.Status);

                updateOneModels.Add(new UpdateOneModel<QueueLock>(filter, updateValue));
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

        public async Task<long> DeleteQueueLockCompleted(Guid docInstanceId, Guid workflowStepInstanceId, Guid? docTypeFieldInstanceId = null,
            Guid? docFieldValueInstanceId = null)
        {
            var filter1 = Builders<QueueLock>.Filter.Eq(x => x.DocInstanceId, docInstanceId);
            var filter2 = Builders<QueueLock>.Filter.Eq(x => x.WorkflowStepInstanceId, workflowStepInstanceId);
            var filter3 = Builders<QueueLock>.Filter.Eq(x => x.DocTypeFieldInstanceId, docTypeFieldInstanceId);
            var filter4 = Builders<QueueLock>.Filter.Eq(x => x.DocFieldValueInstanceId, docFieldValueInstanceId);
            return await DeleteOneAsync(filter1 & filter2 & filter3 & filter4);
        }
    }
}
