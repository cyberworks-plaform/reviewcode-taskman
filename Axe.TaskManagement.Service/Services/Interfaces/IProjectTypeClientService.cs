using System.Collections.Generic;
using System.Threading.Tasks;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IProjectTypeClientService
    {
        Task<GenericResponse<IEnumerable<SelectItemDto>>> GetDropdownExternalAsync(bool onlyActive = true, string accessToken = null);
    }
}