using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using System;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/outbox-intergration-event")]
    [Authorize]
    public class OutboxIntegrationEventController : InfrastructureController
    {
        #region Initialize
        protected readonly IOutBoxIntegrationEventService _service;
        public OutboxIntegrationEventController(IOutBoxIntegrationEventService service)
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
        [Route("get-total-count")]
        public async Task<IActionResult> GetTotalCount()
        {
            return ResponseResult(await _service.TotalCountAsync());
        }

        [HttpGet]
        [Route("get-total-and-status-count")]
        public async Task<IActionResult> GetTotalAndStatusCountAsync()
        {
            return ResponseResult(await _service.GetTotalAndStatusCountAsync());

        }
        [HttpGet]
        [Route("get-by-id/{id}")]
        public async Task<IActionResult> GetById(long id)
        {
            return ResponseResult(await _service.GetByIdAsync(id));
        }
    }
}
