using Axe.TaskManagement.Service.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Background
{
    public class RedisKeyEvent : BackgroundService
    {
        public IServiceProvider Services { get; }
        private string EXPIRED_KEYS_CHANNEL = "__keyevent@0__:expired"; //=> chanel key even expired
        private readonly IConnectionMultiplexer _connectionMultiplexer;

        public RedisKeyEvent(IServiceProvider services, IConnectionMultiplexer connectionMultiplexer)
        {
            Services = services;
            _connectionMultiplexer = connectionMultiplexer;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information(
                "Redis Key event service is working.");

            using (var scope = Services.CreateScope())
            {
                var scopedProcessingService =
                    scope.ServiceProvider
                        .GetRequiredService<IRecallJobWorkerService>();
                await scopedProcessingService.ReCallAllJob();
            }


            var subscriber = _connectionMultiplexer.GetSubscriber();
            await subscriber.SubscribeAsync(EXPIRED_KEYS_CHANNEL, (async (channel, key) =>
            {
                await HandleKeyRedis(key);
            }));

        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            Log.Information("Redis Key event service Hosted Service is stopping.");
            await base.StopAsync(stoppingToken);
        }

        private async Task HandleKeyRedis(string key)
        {
            if (key.EndsWith("$@$RecallJob"))
            {
                var parseUser = Guid.TryParse(key.Split("$@$").Skip(1).First(), out Guid userInstanceId);
                var parseTurn = Guid.TryParse(key.Split("$@$").Skip(2).First(), out Guid turnInstanceId);
                var accessToken = key.Split("$@$").Skip(3).First();
                if (parseUser && parseTurn && userInstanceId != Guid.Empty && turnInstanceId != Guid.Empty)
                {
                    using (var scope = Services.CreateScope())
                    {
                        var scopedProcessingService =
                            scope.ServiceProvider
                                .GetRequiredService<IRecallJobWorkerService>();
                        await scopedProcessingService.DoWork(userInstanceId, turnInstanceId, accessToken);
                    }
                }
            }
        }
    }
}
