using Axe.Utility.Dtos;
using Ce.Constant.Lib.Dtos;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface ITransactionClientService
    {
        Task<GenericResponse<long>> AddTransactionAsync(TransactionAddDto model, string accessToken = null);

        Task<GenericResponse<int>> AddMultiTransactionAsync(TransactionAddMultiDto model, string accessToken = null);
    }
}