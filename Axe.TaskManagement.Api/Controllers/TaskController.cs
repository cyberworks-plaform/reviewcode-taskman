using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.Dtos;
using Axe.Utility.Enums;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/task")]
    [Authorize]
    public class TaskController : MongoBaseController<ITaskService, TaskEntity, TaskDto>
    {
        public TaskController(ITaskService service) : base(service)
        {
        }

        [HttpDelete]
        [Route("delete-by-doc-instance-id/{docInstanceId}")]
        public async Task<IActionResult> DeleteByDocInstanceIdAsync(Guid docInstanceId)
        {
            return ResponseResult(await _service.DeleteByDocInstanceIdAsync(docInstanceId));
        }

        [HttpPut]
        [Route("change-status")]
        public async Task<IActionResult> ChangeStatus(string id, short newStatus = (short)EnumTask.Status.Processing)
        {
            return ResponseResult(await _service.ChangeStatus(id, newStatus));
        }

        [HttpPut]
        [Route("update-progress-value")]
        public async Task<IActionResult> UpdateProgressValue(UpdateTaskStepProgressDto updateTaskStepProgress)
        {
            return ResponseResult(await _service.UpdateProgressValue(updateTaskStepProgress));
        }

        [HttpGet]
        [Route("get-by-doc-instance-id/{docInstanceId}")]
        public async Task<IActionResult> GetByDocInstanceId(Guid docInstanceId)
        {
            return ResponseResult(await _service.GetByDocInstanceId(docInstanceId));
        }
    }
}
