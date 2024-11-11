using System.Threading.Tasks;
using System;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface ICommonConsumerService : IDisposable
{
    Task<string> GetExchangeName(Type eventHandlerType);
}