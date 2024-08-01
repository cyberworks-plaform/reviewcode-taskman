﻿using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.MongoDbBase.Interfaces;
using Ce.Constant.Lib.Dtos;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces
{
    public interface IComplainService : IMongoBaseService<Complain, ComplainDto>
    {
        Task<GenericResponse<ComplainDto>> GetByJobCode(string code);
        Task<GenericResponse<ComplainDto>> GetByInstanceId(string instanceId);
        Task<GenericResponse<ComplainDto>> CreateOrUpdateComplain(ComplainDto complainDto, string accessToken = null);
        Task<GenericResponse<HistoryComplainDto>> GetHistoryComplainByUser(PagingRequest request, string actionCode, string accessToken);
        Task<GenericResponse<HistoryComplainDto>> GetPaging(PagingRequest request, string accessToken);
        Task<GenericResponse<ComplainDto>> GetByInstanceId(Guid instanceId);
        Task<GenericResponse<List<ComplainDto>>> GetByInstanceIds(List<Guid> instanceIds);
    }
}
