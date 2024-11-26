using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/extended-inbox-intergration-event")]
    [Authorize]
    public class ExtendedInboxIntegrationEventController : InfrastructureController
    {
        #region Initialize
        protected readonly IExtendedInboxIntegrationEventService _service;
        public ExtendedInboxIntegrationEventController(IExtendedInboxIntegrationEventService service)
        {
            _service = service;
        }
        #endregion

        [HttpPost]
        [Route("get-paging")]
        public virtual async Task<IActionResult> GetPaging(PagingRequest request)
        {
            return ResponseResult(await _service.GetPagingAsync(request));
        }

        [HttpGet]
        [Route("get-by-intergration-event-id/{intergrationEventId}")]
        public async Task<IActionResult> GetById(Guid intergrationEventId)
        {
            return ResponseResult(await _service.GetByIntergrationEventIdAsync(intergrationEventId));
        }

        [HttpGet]
        [Route("get-total-count")]
        public async Task<IActionResult> GetTotalCount()
        {
            return ResponseResult(await _service.TotalCountAsync());
        }

        [HttpGet]
        [Route("update-multi-priority")]
        public async Task<IActionResult> UpdateMultiPriority(Guid projectInstanceId, short priority, int batchSize = 100)
        {
            return ResponseResult(await _service.UpdateMultiPriorityAsync(projectInstanceId, priority, batchSize));
        }
    }
}
