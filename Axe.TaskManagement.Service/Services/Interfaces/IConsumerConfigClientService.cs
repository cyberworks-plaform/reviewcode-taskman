using Ce.Constant.Lib.Dtos;
using System.Threading.Tasks;
using Axe.TaskManagement.Service.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IConsumerConfigClientService
{
    Task<GenericResponse<ExchangeConfigDto>> GetExchangeConfig(string exchangeName, string accessToken = null);
}