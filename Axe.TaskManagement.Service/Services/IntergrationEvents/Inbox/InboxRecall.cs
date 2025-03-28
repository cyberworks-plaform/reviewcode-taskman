﻿using Axe.TaskManagement.Data.Repositories.Interfaces;
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

        private readonly TimeSpan _timeSpan = TimeSpan.FromMinutes(5);
        private readonly TimeSpan _inboxRecallHangingMessage = TimeSpan.FromMinutes(30);
        private bool _isFirstTime = true;

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
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            Log.Information("InboxRecall background service start working");
            return base.StartAsync(cancellationToken);
        }
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            Log.Information("InboxRecall background service stop working");
            await base.StopAsync(stoppingToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (_isFirstTime) // if is first time, wait 1 minute before start => this is strick to avoid service crash at startup time
                    {
                        _isFirstTime = false;
                        await Task.Delay(TimeSpan.FromMinutes(1));
                        continue;
                    }

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
                    Log.Error(ex, $"Error in InboxRecall background service: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(_timeSpan, stoppingToken);
                }
            }
        }
    }
}
