using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Api.Controllers
{
    [ApiController]
    [Route("api/axe-task-management/sequence-job")]
    [Authorize]
    public class SequenceJobController : MongoBaseController<ISequenceJobService, SequenceJob, SequenceJobDto>
    {
        #region Initialize

        public SequenceJobController(ISequenceJobService service) : base(service)
        {
        }

        #endregion

        [HttpGet]
        [Route("get-sequence-value")]
        public async Task<IActionResult> GetSequenceValue(string sequenceName)
        {
            return ResponseResult(await _service.GetSequenceValue(sequenceName));
        }
    }
}
