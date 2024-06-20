using System.Threading.Tasks;
using Ce.Constant.Lib.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IUserConfigClientService
    {
        Task<GenericResponse<string>> GetValueByCodeAsync(string code, string accessToken = null);

        Task<GenericResponse<bool>> SetValueByCodeAsync(string code, string val, string col = "StringVal", string accessToken = null);
    }
}