using Ce.Common.Lib.Interfaces;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Data.DataAccess
{
    public class AxeTaskManagementDbContext : DbContext
    {
        private readonly IUserPrincipalService _userPrincipalService;
        private readonly string _providerName;
        private static IConfigurationRoot _configuration;

        public AxeTaskManagementDbContext(DbContextOptions<AxeTaskManagementDbContext> options, IUserPrincipalService userPrincipalService) : base(options)
        {
            _userPrincipalService = userPrincipalService;
            if (_configuration == null)
            {
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                _configuration = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: true)
                    .Build();
            }
            _providerName = _configuration.GetConnectionString("ProviderName") ?? "";
        }

        // Declaire DbSets
        public DbSet<OutboxIntegrationEvent> OutboxIntegrationEvents { get; set; }
      
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            if (_providerName == ProviderTypeConstants.Postgre)
            {
                modelBuilder.HasDefaultSchema("public");
                // Add the Postgres Extension for UUID generation
                modelBuilder.HasPostgresExtension("uuid-ossp");

                // Add the Postgres Extension for UUID Index
                modelBuilder.HasPostgresExtension("btree_gin");

                foreach (var pb in modelBuilder.Model.GetEntityTypes()
                    .Where(p => typeof(IInstanceId).IsAssignableFrom(p.ClrType)) //Check implement IInstanceId
                    .SelectMany(t => t.GetProperties())
                    .Where(p => p.Name == nameof(IInstanceId.InstanceId)) // Check column Name
                                                                          //.Select(p => modelBuilder.Entity(p.DeclaringEntityType.ClrType).Property(p.Name))
                    )
                {
                    modelBuilder.Entity(pb.DeclaringEntityType.ClrType).Property(pb.Name).HasDefaultValueSql("uuid_generate_v4()"); // auto generate PostgreSQL UUID                   
                    var index = pb.DeclaringEntityType.AddIndex(pb);
                    index.SetMethod("gin");
                }
            }
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var modifiedEntries = ChangeTracker.Entries()
                    .Where(x => x.Entity is IAuditable
                                && (x.State == EntityState.Added || x.State == EntityState.Modified)).ToList();

                if (modifiedEntries.Any())
                {
                    foreach (var entry in modifiedEntries)
                    {
                        var entity = entry.Entity as IAuditable;
                        if (entity == null) continue;

                        if (entry.State == EntityState.Added)
                        {
                            if (entity.CreatedBy == null || entity.CreatedBy == Guid.Empty)
                            {
                                if (_userPrincipalService != null)
                                {
                                    entity.CreatedBy = _userPrincipalService.UserInstanceId;
                                }
                            }
                            entity.CreatedDate = DateTime.UtcNow;
                        }
                        else
                        {
                            if (_userPrincipalService != null)
                            {
                                entity.LastModifiedBy = _userPrincipalService.UserInstanceId;
                            }
                            entity.LastModificationDate = DateTime.UtcNow;
                        }
                    }
                }

                // Multi-tenant
                var tenantModifiedEntries = ChangeTracker.Entries()
                    .Where(x => x.Entity is IMultiTenant && x.State == EntityState.Added).ToList();

                if (tenantModifiedEntries.Any())
                {
                    foreach (var entry in tenantModifiedEntries)
                    {
                        var entity = entry.Entity as IMultiTenant;
                        if (entity == null || entity.TenantId > 0) continue;
                        if (_userPrincipalService != null)
                        {
                            entity.TenantId = _userPrincipalService.TenantId;
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }

            return await base.SaveChangesAsync(true, cancellationToken);
        }

    }
}
