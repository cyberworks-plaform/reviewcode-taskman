using AutoMapper;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.Utility.EntityExtensions;
using Ce.Constant.Lib.Dtos;
using MongoDB.Bson;

namespace Axe.TaskManagement.Service.Mappers
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<Job, JobDto>(MemberList.Destination)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()))
                .ForMember(dest => dest.TaskId, opt => opt.MapFrom(src => src.TaskId.ToString()));
            CreateMap<JobDto, Job>(MemberList.Source)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => new ObjectId(src.Id)))
                .ForMember(dest => dest.TaskId, opt => opt.MapFrom(src => new ObjectId(src.TaskId)));

            CreateMap<Job, LogJobDto>(MemberList.Destination)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()));
            CreateMap<LogJobDto, Job>(MemberList.Source)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => new ObjectId(src.Id)));

            CreateMap<Job, PrevJobInfo>(MemberList.Destination)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()));

            CreateMap<TaskEntity, TaskDto>(MemberList.Destination)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()));
            CreateMap<TaskDto, TaskEntity>(MemberList.Source)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => new ObjectId(src.Id)));

            CreateMap<QueueLock, QueueLockDto>(MemberList.Destination)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()));
            CreateMap<QueueLockDto, QueueLock>(MemberList.Source)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => new ObjectId(src.Id)));

            CreateMap<DocErrorExtension, DocErrorDto>(MemberList.Source);
            CreateMap<ProjectCountExtension, ProjectCountExtensionDto>(MemberList.Source);

            CreateMap<Complain, ComplainDto>(MemberList.Destination)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => src.Id.ToString()));
            CreateMap<ComplainDto, Complain>(MemberList.Source)
                .ForMember(dest => dest.Id, opt => opt.MapFrom(src => new ObjectId(src.Id)));

            //Inbox
            CreateMap<ExtendedInboxIntegrationEventDto, ExtendedInboxIntegrationEvent>();
            CreateMap<ExtendedInboxIntegrationEvent, ExtendedInboxIntegrationEventDto>();

            //Outbox
            CreateMap<OutboxIntegrationEventDto, OutboxIntegrationEvent>();
            CreateMap<OutboxIntegrationEvent, OutboxIntegrationEventDto>();

            CreateMap<DocItem, StoredDocItem>();
            CreateMap<StoredDocItem, DocItem>();
        }
    }
}
