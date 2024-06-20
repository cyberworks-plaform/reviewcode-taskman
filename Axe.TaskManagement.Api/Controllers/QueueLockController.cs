using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/queue-lock")]
    [Authorize]
    public class QueueLockController : MongoBaseController<IQueueLockService, QueueLock, QueueLockDto>
    {
        public QueueLockController(IQueueLockService service) : base(service)
        {
        }

        [HttpPut]
        [Route("upsert-multi")]
        public virtual async Task<IActionResult> UpSertMultiQueueLock([FromBody] List<QueueLockDto> models)
        {
            if (ModelState.IsValid)
            {
                return ResponseResult(await _service.UpSertMultiQueueLockAsync(models));
            }

            return BadRequest(ModelState);
        }

        [HttpDelete]
        [Route("delete-queue-lock-completed")]
        public virtual async Task<IActionResult> DeleteQueueLockCompleted(Guid docInstanceId, Guid workflowStepInstanceId, Guid? docTypeFieldInstanceId = null, Guid? docFieldValueInstanceId = null)
        {
            return ResponseResult(await _service.DeleteQueueLockCompleted(docInstanceId, workflowStepInstanceId, docTypeFieldInstanceId, docFieldValueInstanceId));
        }
    }
}
