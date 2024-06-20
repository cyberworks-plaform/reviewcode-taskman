using AutoMapper;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using System;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class SequenceJobService : MongoBaseService<SequenceJob, SequenceJobDto>, ISequenceJobService
    {
        private readonly ISequenceJobRepository _repository;

        public SequenceJobService(ISequenceJobRepository repos, IMapper mapper, IUserPrincipalService userPrincipalService) : base(repos, mapper, userPrincipalService)
        {
            _repository = repos;
        }

        public async Task<GenericResponse<long>> GetSequenceValue(string sequenceName)
        {
            GenericResponse<long> response;
            try
            {
                response = GenericResponse<long>.ResultWithData(await _repository.GetSequenceValue(sequenceName));
            }
            catch (Exception ex)
            {
                response = GenericResponse<long>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
            }
            return response;
        }
    }
}
