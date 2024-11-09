using Axe.TaskManagement.Data.Repositories.Implementations;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Axe.TaskManagement.Data.Configs
{
    public static class DependencyInjectionReposExtension
    {
        public static void DependencyInjectionRepos(this IServiceCollection services)
        {
            // Add scope repository
            services.AddScoped<ITaskRepository, TaskRepository>();
            services.AddScoped<IJobRepository, JobRepository>();
            services.AddScoped<ISequenceJobRepository, SequenceJobRepository>();
            services.AddScoped<IQueueLockRepository, QueueLockRepository>();
            services.AddScoped<IComplainRepository, ComplainRepository>();
            services.AddScoped<ISequenceComplainRepository, SequenceComplainRepository>();

            services.AddScoped<IOutboxIntegrationEventRepository, OutboxIntegrationEventRepository>();
            services.AddScoped<IExtendedInboxIntegrationEventRepository, ExtendedInboxIntegrationEventRepository>();
        }
    }
}
