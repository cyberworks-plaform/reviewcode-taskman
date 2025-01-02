using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class OutBoxIntegrationEventService : IOutBoxIntegrationEventService
    {
        private readonly IEventBus _eventBus;
        private readonly IOutboxIntegrationEventRepository _outboxIntegrationEventRepository;
        private readonly IMapper _mapper;

        public OutBoxIntegrationEventService(
            IEventBus eventBus,
            IMapper mapper,
            IOutboxIntegrationEventRepository outboxIntegrationEventRepository
            )
        {
            _eventBus = eventBus;
            _mapper = mapper;
            _outboxIntegrationEventRepository = outboxIntegrationEventRepository;
        }
        public virtual async Task<GenericResponse<PagedList<OutboxIntegrationEventDto>>> GetPagingAsync(PagingRequest request)
        {
            if (request.PageInfo == null)
            {
                request.PageInfo = new PageInfo
                {
                    PageIndex = 1,
                    PageSize = 10
                };
            }

            if (request.PageInfo.PageIndex <= 0)
            {
                return GenericResponse<PagedList<OutboxIntegrationEventDto>>.ResultWithError(400, null, "Page index must be greater or than 1");
            }

            if (request.PageInfo.PageSize < 0)
            {
                return GenericResponse<PagedList<OutboxIntegrationEventDto>>.ResultWithError(400, null, "Page size must be greater or than 0");
            }

            GenericResponse<PagedList<OutboxIntegrationEventDto>> result;
            try
            {
                PagedList<OutboxIntegrationEvent> pagedList = await _outboxIntegrationEventRepository.GetPagingCusAsync(request);
                List<OutboxIntegrationEventDto> data = _mapper.Map<List<OutboxIntegrationEventDto>>(pagedList.Data);
                result = GenericResponse<PagedList<OutboxIntegrationEventDto>>.ResultWithData(new PagedList<OutboxIntegrationEventDto>
                {
                    Data = data,
                    PageIndex = pagedList.PageIndex,
                    PageSize = pagedList.PageSize,
                    TotalCount = pagedList.TotalCount,
                    TotalFilter = pagedList.TotalFilter,
                    TotalPages = pagedList.TotalPages
                });
            }
            catch (Exception ex)
            {
                result = GenericResponse<PagedList<OutboxIntegrationEventDto>>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }

        public virtual async Task<GenericResponse<long>> TotalCountAsync()
        {
            GenericResponse<long> result;
            try
            {
                result = GenericResponse<long>.ResultWithData(await _outboxIntegrationEventRepository.TotaCountAsync());
            }
            catch (Exception ex)
            {
                result = GenericResponse<long>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<Dictionary<int, long>>> GetTotalAndStatusCountAsync()
        {
            GenericResponse<Dictionary<int, long>> result;
            try
            {
                result = GenericResponse<Dictionary<int, long>>.ResultWithData(await _outboxIntegrationEventRepository.GetTotalAndStatusCountAsync());
            }
            catch (Exception ex)
            {
                result = GenericResponse<Dictionary<int, long>>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<OutboxIntegrationEventDto>> GetByIdAsync(long id)
        {
            GenericResponse<OutboxIntegrationEventDto> result;
            try
            {
                OutboxIntegrationEvent source = await _outboxIntegrationEventRepository.GetByIdAsync(id);
                result = GenericResponse<OutboxIntegrationEventDto>.ResultWithData(_mapper.Map<OutboxIntegrationEventDto>(source));
            }
            catch (Exception ex)
            {
                result = GenericResponse<OutboxIntegrationEventDto>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<long>> AddAsync(OutboxIntegrationEvent model)
        {
            GenericResponse<long> result;
            try
            {
                result = GenericResponse<long>.ResultWithData(await _outboxIntegrationEventRepository.AddAsync(model));
            }
            catch (Exception ex)
            {
                result = GenericResponse<long>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<OutboxIntegrationEvent>> AddAsyncV2(OutboxIntegrationEvent model)
        {
            GenericResponse<OutboxIntegrationEvent> result;
            try
            {
                result = GenericResponse<OutboxIntegrationEvent>.ResultWithData(await _outboxIntegrationEventRepository.AddAsyncV2(model));
            }
            catch (Exception ex)
            {
                result = GenericResponse<OutboxIntegrationEvent>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
    }
}
