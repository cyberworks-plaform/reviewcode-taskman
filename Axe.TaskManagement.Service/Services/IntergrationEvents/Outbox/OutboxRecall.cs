using Axe.TaskManagement.Data.Repositories.Interfaces;
using Ce.Constant.Lib.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.IntergrationEvents.Outbox
{
    /// <summary>
    /// Thu hồi các message trong outbox đang ở treo ở trạng thái sending (status = 1) lâu hơn 5 phút
    /// Tự động check 5 phút 1 lần
    /// </summary>
    public class OutboxRecall : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;

        private readonly TimeSpan _timeSpan = TimeSpan.FromMinutes(5); // 5 phút chạy 1 lần
        private bool _isFirstTime = true;

        public OutboxRecall(IConfiguration configuration, IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }
        public override Task StartAsync(CancellationToken cancellationToken)
        {
            Log.Information("OutboxRecall background service start working");
            return base.StartAsync(cancellationToken);
        }
        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            Log.Information("OutboxRecall background service stop working");
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
                    using (var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxIntegrationEventRepository>())
                    {
                        var recallOutboxMesssages = await outboxRepo.GetsRecallOutboxIntegrationEventAsync((int)_timeSpan.TotalMinutes);
                        foreach (var message in recallOutboxMesssages)
                        {
                            try
                            {
                                message.Status = (short)EnumEventBus.PublishMessageStatus.Nack;
                                message.LastModificationDate = DateTime.UtcNow;
                                await outboxRepo.UpdateAsync(message);
                            }
                            catch (Exception exMessage)
                            {
                                //do nothing just log error
                                Log.Error(exMessage, $"Error in recall messsage: {exMessage.Message}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"Error in OutboxRecall background service: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(_timeSpan, stoppingToken);
                }
            }
        }
    }
}
