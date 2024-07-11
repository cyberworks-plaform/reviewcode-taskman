using Axe.TaskManagement.Service.Services.Implementations;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Outbox;
using Microsoft.Extensions.DependencyInjection;

namespace Axe.TaskManagement.Service.Configs
{
    public static class DependencyInjectionServiceExtension
    {
        public static void DependencyInjectionService(this IServiceCollection services)
        {
            // Add scope service
            services.AddScoped<ITaskService, TaskService>();
            services.AddScoped<IJobService, JobService>();
            services.AddScoped<ISequenceJobService, SequenceJobService>();
            services.AddScoped<IRecallJobWorkerService, RecallJobWorkerService>();
            services.AddScoped<IQueueLockService, QueueLockService>();

            // Common service
            services.AddScoped<IMoneyService, MoneyService>();

            // Client service
            services.AddSingleton<IProjectTypeClientService, ProjectTypeClientService>();
            services.AddSingleton<IProjectClientService, ProjectClientService>();
            services.AddSingleton<IUserProjectClientService, UserProjectClientService>();
            services.AddSingleton<IDocClientService, DocClientService>();
            services.AddSingleton<IDocFieldValueClientService, DocFieldValueClientService>();
            services.AddSingleton<IBatchClientService, BatchClientService>();
            services.AddSingleton<IBussinessConfigClientService, BussinessConfigClientService>();
            services.AddSingleton<IUserConfigClientService, UserConfigClientService>();
            services.AddSingleton<ITransactionClientService, TransactionClientService>();
            services.AddSingleton<IProjectStatisticClientService, ProjectStatisticClientService>();
            services.AddSingleton<IExternalProviderServiceConfigClientService, ExternalProviderServiceConfigClientService>();

            // Outbox
            services.AddHostedService<OutboxPublisher>();
        }
    }
}
