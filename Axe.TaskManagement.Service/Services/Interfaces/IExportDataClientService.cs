using Axe.TaskManagement.Service.Dtos;
using Ce.Constant.Lib.Dtos;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IExportDataClientService
{
    Task<GenericResponse<ExportDataDto>> GetById(int id, string accessToken = null);
    Task<GenericResponse<int>> UpdateAsync(ExportDataDto model, string accessToken = null);
}