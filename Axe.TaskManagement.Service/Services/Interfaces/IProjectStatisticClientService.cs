using Axe.Utility.Dtos;
using Ce.Constant.Lib.Dtos;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IProjectStatisticClientService
    {
        Task<GenericResponse<long>> UpdateProjectStatisticAsync(ProjectStatisticUpdateProgressDto model, string accessToken = null);

        Task<GenericResponse<long>> UpdateMultiProjectStatisticAsync(ProjectStatisticUpdateMultiProgressDto model, string accessToken = null);
    }
}