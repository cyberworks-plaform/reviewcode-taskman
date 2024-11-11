using Axe.TaskManagement.Data.Repositories.Interfaces;
using Ce.Constant.Lib.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Inbox
{
    public class InboxRecall : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeSpan _timeSpan = TimeSpan.FromMinutes(30);
        private readonly TimeSpan _inboxRecallHangingMessage = TimeSpan.FromHours(1);

        public InboxRecall(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
            if (configuration["RabbitMq:InboxRecallHangingMessage"] != null)
            {
                if (TimeSpan.TryParse(configuration["RabbitMq:InboxRecallHangingMessage"], out var temp))
                {
                    _inboxRecallHangingMessage = temp;
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceScopeFactory.CreateScope();
                    using (var inboxIntegrationEventRepository = scope.ServiceProvider.GetRequiredService<IExtendedInboxIntegrationEventRepository>())
                    {
                        var recallInboxEvents = await inboxIntegrationEventRepository.GetsRecallInboxIntegrationEventAsync((int)_inboxRecallHangingMessage.TotalMinutes);
                        foreach (var recallInboxEvent in recallInboxEvents)
                        {
                            recallInboxEvent.Status = (short)EnumEventBus.ConsumMessageStatus.Nack;
                            recallInboxEvent.Message = $"InboxRecallHangingMessage: {_inboxRecallHangingMessage}";
                            await inboxIntegrationEventRepository.UpdateAsync(recallInboxEvent);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Error in recall inbox event: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(_timeSpan, stoppingToken);
                }
            }
        }
    }
}
