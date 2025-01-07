using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class ExtendedInboxIntegrationEventService : IExtendedInboxIntegrationEventService
    {
        private readonly IExtendedInboxIntegrationEventRepository _repository;
        private readonly IMapper _mapper;

        public ExtendedInboxIntegrationEventService(
            IExtendedInboxIntegrationEventRepository repos,
            IMapper mapper,
            IUserPrincipalService userPrincipalService)
        {
            _mapper = mapper;
            _repository = repos;
        }
        public virtual async Task<GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>>> GetPagingAsync(PagingRequest request)
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
                return GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>>.ResultWithError(400, null, "Page index must be greater or than 1");
            }

            if (request.PageInfo.PageSize < 0)
            {
                return GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>>.ResultWithError(400, null, "Page size must be greater or than 0");
            }

            GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>> result;
            try
            {
                PagedList<ExtendedInboxIntegrationEvent> pagedList = await _repository.GetPagingCusAsync(request);
                List<ExtendedInboxIntegrationEventDto> data = _mapper.Map<List<ExtendedInboxIntegrationEventDto>>(pagedList.Data);
                result = GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>>.ResultWithData(new PagedList<ExtendedInboxIntegrationEventDto>
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
                result = GenericResponse<PagedList<ExtendedInboxIntegrationEventDto>>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<ExtendedInboxIntegrationEventDto>> GetByIntergrationEventIdAsync(Guid intergrationEventId)
        {
            GenericResponse<ExtendedInboxIntegrationEventDto> result;
            try
            {
                ExtendedInboxIntegrationEvent source = await _repository.GetByIntergrationEventIdAsync(intergrationEventId);
                result = GenericResponse<ExtendedInboxIntegrationEventDto>.ResultWithData(_mapper.Map<ExtendedInboxIntegrationEventDto>(source));
            }
            catch (Exception ex)
            {
                result = GenericResponse<ExtendedInboxIntegrationEventDto>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<IEnumerable<ExtendedInboxIntegrationEventDto>>> GetByIdsAsync(string ids)
        {
            GenericResponse<IEnumerable<ExtendedInboxIntegrationEventDto>> result;
            try
            {
                IEnumerable<ExtendedInboxIntegrationEvent> source = await _repository.GetByIdsAsync(ids);
                result = GenericResponse<IEnumerable<ExtendedInboxIntegrationEventDto>>.ResultWithData(_mapper.Map<IEnumerable<ExtendedInboxIntegrationEvent>, IEnumerable<ExtendedInboxIntegrationEventDto>>(source));
            }
            catch (Exception ex)
            {
                result = GenericResponse<IEnumerable<ExtendedInboxIntegrationEventDto>>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public virtual async Task<GenericResponse<long>> TotalCountAsync()
        {
            GenericResponse<long> result;
            try
            {
                result = GenericResponse<long>.ResultWithData(await _repository.TotaCountAsync());
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
                result = GenericResponse<Dictionary<int, long>>.ResultWithData(await _repository.GetTotalAndStatusCountAsync());
            }
            catch (Exception ex)
            {
                result = GenericResponse<Dictionary<int, long>>.ResultWithError(400, ex.StackTrace, ex.Message);
            }

            return result;
        }

        public async Task<GenericResponse<int>> UpdateMultiPriorityAsync(string serviceCode, string exchangeName, Guid projectInstanceId, short priority, int batchSize = 100)
        {
            GenericResponse<int> result;
            try
            {
                result = GenericResponse<int>.ResultWithData(await _repository.UpdateMultiPriorityAsync(serviceCode, exchangeName, projectInstanceId, priority, batchSize));
            }
            catch (Exception ex)
            {
                result = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<int>> ResetRetryCountAsync(Guid intergrationEventId, short retryCount)
        {
            GenericResponse<int> result;
            try
            {
                result = GenericResponse<int>.ResultWithData(await _repository.ResetRetryCountAsync(intergrationEventId, retryCount));
            }
            catch (Exception ex)
            {
                result = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<int>> ResetAllRetryCountAsync(short status, short retryCount)
        {
            GenericResponse<int> result;
            try
            {
                result = GenericResponse<int>.ResultWithData(await _repository.ResetAllRetryCountAsync(status, retryCount));
            }
            catch (Exception ex)
            {
                result = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return result;
        }
        public async Task<GenericResponse<int>> ResetMultiRetryCountsAsync(string intergrationEventIds, short retryCount)
        {
            GenericResponse<int> result;
            try
            {
                if (!string.IsNullOrEmpty(intergrationEventIds))
                {
                    var lstIntergrationEventId = JsonConvert.DeserializeObject<List<Guid>>(intergrationEventIds);
                    if (intergrationEventIds != null && intergrationEventIds.Any())
                    {
                        result = GenericResponse<int>.ResultWithData(await _repository.ResetMultiRetryCountsAsync(lstIntergrationEventId, retryCount));
                    }
                    else
                    {
                        result = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, "IntergrationEventIds is empty", "IntergrationEventIds is empty");
                    }
                }
                else
                {
                    result = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, "IntergrationEventIds is empty", "IntergrationEventIds is empty");
                }
            }
            catch (Exception ex)
            {
                result = GenericResponse<int>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }

            return result;
        }
    }
}
