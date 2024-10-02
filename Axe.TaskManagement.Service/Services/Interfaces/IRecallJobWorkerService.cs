using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IRecallJobWorkerService
    {
        Task DoWork(Guid userInstanceId, Guid turnInstanceId, string accessToken);
        Task<string> ReCallAllJob();
        Task RecallJobByTurn(Guid userInstanceId, Guid turnInstanceId, string accessToken = null);
    }
}
