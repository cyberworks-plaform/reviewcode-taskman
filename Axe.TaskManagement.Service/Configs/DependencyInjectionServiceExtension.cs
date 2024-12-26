using Axe.TaskManagement.Service.Services.Implementations;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Inbox;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Outbox;
using Axe.TaskManagement.Service.Services.IntergrationEvents.ProcessEvent;
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
            services.AddScoped<IReportService, ReportService>();
            services.AddScoped<ISequenceJobService, SequenceJobService>();
            services.AddScoped<IRecallJobWorkerService, RecallJobWorkerService>();
            services.AddScoped<IQueueLockService, QueueLockService>();
            services.AddScoped<IComplainService, ComplainService>();
            services.AddScoped<ISequenceComplainService, SequenceComplainService>();
            services.AddScoped<IExtendedInboxIntegrationEventService, ExtendedInboxIntegrationEventService>();

            // Common service
            services.AddScoped<IMoneyService, MoneyService>();
            services.AddTransient<ICommonConsumerService, CommonConsumerService>();

            // Client service
            services.AddSingleton<IProjectTypeClientService, ProjectTypeClientService>();
            services.AddSingleton<IProjectClientService, ProjectClientService>();
            services.AddSingleton<IUserProjectClientService, UserProjectClientService>();
            services.AddSingleton<IDocClientService, DocClientService>();
            services.AddSingleton<IDocTypeFieldClientService, DocTypeFieldClientService>();
            services.AddSingleton<IDocFieldValueClientService, DocFieldValueClientService>();
            services.AddSingleton<IBatchClientService, BatchClientService>();
            services.AddSingleton<IBussinessConfigClientService, BussinessConfigClientService>();
            services.AddSingleton<IUserConfigClientService, UserConfigClientService>();
            services.AddSingleton<ITransactionClientService, TransactionClientService>();
            services.AddSingleton<IProjectStatisticClientService, ProjectStatisticClientService>();
            services.AddSingleton<IExternalProviderServiceConfigClientService, ExternalProviderServiceConfigClientService>();
            services.AddSingleton<IExtendedMessagePriorityConfigClientService, ExtendedMessagePriorityConfigClientService>();

            // Outbox
            services.AddHostedService<OutboxPublisher>();
            services.AddScoped<IOutBoxIntegrationEventService, OutBoxIntegrationEventService>();

            // Inbox
            services.AddHostedService<InboxConsumer>();
            services.AddHostedService<InboxRecall>();

            // Process Event
            services.AddTransient<IAfterProcessCheckFinalProcessEvent, AfterProcessCheckFinalProcessEvent>();
            services.AddTransient<IAfterProcessDataCheckProcessEvent, AfterProcessDataCheckProcessEvent>();
            services.AddTransient<IAfterProcessDataConfirmProcessEvent, AfterProcessDataConfirmProcessEvent>();
            services.AddTransient<IAfterProcessDataEntryBoolProcessEvent, AfterProcessDataEntryBoolProcessEvent>();
            services.AddTransient<IAfterProcessDataEntryProcessEvent, AfterProcessDataEntryProcessEvent>();
            services.AddTransient<IAfterProcessQaCheckFinalProcessEvent, AfterProcessQaCheckFinalProcessEvent>();
            services.AddTransient<IAfterProcessSegmentLabelingProcessEvent, AfterProcessSegmentLabelingProcessEvent>();
            services.AddTransient<IQueueLockProcessEvent, QueueLockProcessEvent>();
            services.AddTransient<IRetryDocProcessEvent, RetryDocProcessEvent>();
            services.AddTransient<ITaskProcessEvent, TaskProcessEvent>();
        }
    }
}
