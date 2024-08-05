using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/complain")]
    [Authorize]
    public class ComplainController : MongoBaseController<IComplainService, Complain, ComplainDto>
    {
        #region Initialize

        public ComplainController(IComplainService service) : base(service)
        {
        }

        #endregion

        [HttpPost]
        [Route("create-or-update-complain")]
        public async Task<IActionResult> CreateOrUpdateComplain([FromBody] ComplainDto model)
        {
            return ResponseResult(await _service.CreateOrUpdateComplain(model, GetBearerToken()));
        }

        [HttpPost]
        [Route("get-by-job-code")]
        public async Task<IActionResult> GetByJobCode(string code)
        {
            return ResponseResult(await _service.GetByJobCode(code));
        }

        [HttpGet]
        [Route("get-by-instance/{instanceId}")]
        public async Task<IActionResult> GetByInstanceId(Guid instanceId)
        {
            return ResponseResult(await _service.GetByInstanceId(instanceId));
        }

        [HttpPost]
        [Route("get-by-instance-ids")]
        public async Task<IActionResult> GetByInstanceIds(List<Guid> instanceIds)
        {
            return ResponseResult(await _service.GetByInstanceIds(instanceIds));
        }

        /// <summary>
        /// PagingRequest nhận các filter 
        /// 1>UserInstanceId: không truyền sẽ lấy theo token
        /// 2>StartDate
        /// 3>EndDate
        /// 4>DocName
        /// 5>Code: code của job
        /// </summary>
        /// <param name="request"></param>
        /// <param name="actionCode"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("get-history-complain-by-user")]
        public async Task<IActionResult> GetHistoryComplainByUser([FromBody] PagingRequest request, string actionCode)
        {
            return ResponseResult(await _service.GetHistoryComplainByUser(request, actionCode, GetBearerToken()));
        }

        /// <summary>
        /// Get pagging
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        [HttpPost]
        [Route("get-paging")]
        public async Task<IActionResult> GetPaging([FromBody] PagingRequest request)
        {
            return ResponseResult(await _service.GetPaging(request, GetBearerToken()));
        }

        
    }
}
