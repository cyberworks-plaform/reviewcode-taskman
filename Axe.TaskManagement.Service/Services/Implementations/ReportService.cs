﻿using AutoMapper;
using Axe.TaskManagement.Data.EntityExtensions;
using Axe.TaskManagement.Data.Repositories.Implementations;
using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.TaskManagement.Service.Services.IntergrationEvents.Event;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Azure;
using Azure.Core;
using Ce.Auth.Client.Services.Interfaces;
using Ce.Common.Lib.Abstractions;
using Ce.Common.Lib.Caching.Interfaces;
using Ce.Common.Lib.MongoDbBase.Implementations;
using Ce.Common.Lib.Services;
using Ce.Constant.Lib.Dtos;
using Ce.Constant.Lib.Enums;
using Ce.EventBus.Lib.Abstractions;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using Ce.Workflow.Client.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using Microsoft.AspNetCore.Mvc.Rendering;
using Ce.Workflow.Client.Dtos;
using Ce.Auth.Client.Dtos;
using MiniExcelLibs;
using SharpCompress.Common;
using System.IO.Compression;
using SharpCompress.Compressors.Xz;
using Ce.File.Client.Services.Interfaces;
using Ce.File.Client.Dtos;
using Microsoft.AspNetCore.Http;
using Axe.TaskManagement.Model.Enums;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public partial class ReportService : MongoBaseService<Job, JobDto>, IReportService
    {
        private readonly IJobRepository _jobRepository;
        private readonly IProjectClientService _projectClientService;
        private readonly IWorkflowClientService _workflowClientService;
        private readonly IJobService _jobService;
        private readonly IAppUserClientService _appUserClientService;
        private readonly IDocClientService _docClientService;
        private readonly IStgFileClientService _stgFileClientService;
        private readonly IExportDataClientService _exportDataClientService;

        public ReportService(
            IJobRepository repos,
            IMapper mapper,
            IProjectClientService projectClientService,
            IWorkflowClientService workflowClientService,
            IJobService jobService,
            IAppUserClientService appUserClientService,
            IDocClientService docClientService,
            IStgFileClientService stgFileClientService,
            IExportDataClientService exportDataClientService,
            IUserPrincipalService userPrincipalService) : base(repos, mapper, userPrincipalService)
        {
            _jobRepository = repos;
            _projectClientService = projectClientService;
            _workflowClientService = workflowClientService;
            _jobService = jobService;
            _appUserClientService = appUserClientService;
            _docClientService = docClientService;
            _stgFileClientService = stgFileClientService;
            _exportDataClientService = exportDataClientService;
        }
        /// <summary>
        /// Hàm xuất dữ liệu ra file excel. 
        /// Nếu có nhiều hơn 100k bản ghi thì giới hạn mỗi file excel 100k bản ghi
        /// Sau khi xuất xong sẽ nén các file excel thành file zip và gửi api UploadFile zip đó về core và update lại link download DocExport
        /// </summary>
        /// <param name="request"></param>
        /// <param name="actionCode"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task ExportExcelHistoryJobByUserV2(PagingRequest request, string actionCode, int exportDataId, string accessToken)
        {
            IFormFile result = null;
            int maxBatchSize = 100000; // Số lượng bản ghi tối đa trong mỗi file excel
            int currentBatchSize = 0;
            int fileNumber = 1;
            long totalRecord = 0;
            long currentRecord = 0;
            bool isError = false;
            Guid guidFileName = Guid.NewGuid();

            Log.Information($"ExportExcelHistoryJob: Started!");

            var exportDataResponse = await _exportDataClientService.GetById(exportDataId, accessToken);
            if (exportDataResponse != null && exportDataResponse.Data != null)
            {
                //Lấy count toàn bộ bản ghi sẽ xuất để tính Progress
                var totalRecordResponse = await _jobService.GetCountHistoryJobByUserForExportAsync(request, string.Empty, accessToken);
                if (totalRecordResponse != null && totalRecordResponse.Data > 0)
                {
                    totalRecord = totalRecordResponse.Data;
                }

                var exportData = exportDataResponse.Data;
                string tempDirectoryRoot = Path.Combine(Path.GetTempPath(), "ExportExcelHistoryJob");
                string zipFileName = $"JobHistoryFiles_{DateTime.Now.ToString("yyyyMMdd_hhmmss")}.zip";
                string zipFilePath = Path.Combine(Path.GetTempPath(), zipFileName);

                exportData.Name = zipFileName;
                exportData.Status = (short)EnumExportData.Status.Processing;
                //exportData.Note = "Lịch sử xử lý dữ liệu";
                await _exportDataClientService.UpdateAsync(exportData, accessToken);

                string tempDirectory = Path.Combine(tempDirectoryRoot, guidFileName.ToString());
                if (!Directory.Exists(tempDirectory))
                {
                    Directory.CreateDirectory(tempDirectory);
                }

                string currentFilePath = Path.Combine(tempDirectory, $"file_{fileNumber}.xlsx");

                try
                {
                    var allData = new List<Dictionary<string, object>>();
                    string projectInstanceId = "";
                    #region filter & short
                    string statusFilterValue = "";
                    string codeFilterValue = "";
                    string pathFilterValue = "";
                    string actionCodeValue = "";
                    string workFlowStepValue = "";
                    if (request.Filters != null && request.Filters.Count > 0)
                    {
                        //DocPath
                        var pathFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (pathFilter != null)
                        {
                            pathFilterValue = pathFilter.Value.Trim();
                        }
                        //Status
                        var statusFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (statusFilter != null)
                        {
                            statusFilterValue = statusFilter.Value.Trim();
                        }

                        //Code
                        var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (codeFilter != null)
                        {
                            codeFilterValue = codeFilter.Value.Trim();
                        }
                        //ActionCode
                        var actionCodeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ActionCode)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (actionCodeFilter != null)
                        {
                            actionCodeValue = actionCodeFilter.Value.Trim();
                        }
                        var workFlowStepFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.WorkflowStepInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (workFlowStepFilter != null)
                        {
                            workFlowStepValue = workFlowStepFilter.Value.Trim();
                        }
                    }
                    // Nếu không có Status truyền vào thì mặc định Status là Complete
                    var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
                    if (!string.IsNullOrEmpty(codeFilterValue))
                    {
                        baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
                    }
                    //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
                    if (!string.IsNullOrEmpty(pathFilterValue))
                    {
                        baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
                    }
                    if (!string.IsNullOrEmpty(actionCode))
                    {
                        baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                    }
                    else if (!string.IsNullOrEmpty(actionCodeValue))
                    {
                        baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCodeValue);
                    }
                    if (!string.IsNullOrEmpty(workFlowStepValue))
                    {
                        baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.WorkflowStepInstanceId, Guid.Parse(workFlowStepValue));
                    }
                    var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                    if (_userPrincipalService == null)
                    {
                        Log.Error("ExportExcelHistoryJobByUserV2Err: Not authorized _userPrincipalService is null");
                    }

                    var lastFilter = baseFilter;

                    //Apply thêm filter
                    if (request.Filters != null && request.Filters.Count > 0)
                    {
                        //StartDate
                        var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (startDateFilter != null)
                        {
                            var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                            if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                        }

                        //endDate
                        var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (endDateFilter != null)
                        {
                            var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                            if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                        }

                        //DocInstanceId
                        var docInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (docInstanceIdFilter != null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                        }


                        //DocName
                        var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (docNameFilter != null)
                        {
                            if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                            {
                                lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                            }
                            else lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                        }

                        //JobCode
                        var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (codeFilter != null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                        }

                        //NormalState
                        var normalState = request.Filters.Where(_ => _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (normalState != null)
                        {
                            var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                            if (canParse)
                            {
                                switch (actionCode)
                                {
                                    case ActionCodeConstants.DataEntry:
                                        if (stateValue == 0)
                                        {
                                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                        }
                                        if (stateValue > 0)
                                        {
                                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                        }
                                        break;
                                    case ActionCodeConstants.DataCheck:
                                    case ActionCodeConstants.CheckFinal:
                                        if (stateValue == 0)
                                        {
                                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                        }
                                        if (stateValue > 0)
                                        {
                                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                        }
                                        break;
                                    case ActionCodeConstants.QACheckFinal:
                                        if (stateValue == 0)
                                        {
                                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                        }
                                        if (stateValue > 0)
                                        {
                                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                        }
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }

                        //ProjectInstanceId
                        var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (projectInstanceIdFilter != null)
                        {
                            projectInstanceId = projectInstanceIdFilter.Value;
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                        }

                        //UserInstanceId
                        var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (userInstanceIdFilter != null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                        }
                        else
                        {
                            if (projectInstanceIdFilter == null)
                            {
                                lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                            }
                            else
                            {
                                lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                            }
                        }

                        //RightStatus =>//EnumJob.RightStatus
                        var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (statusFilter != null)
                        {
                            var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                            if (canParse && statusValue >= 0)
                            {
                                lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                            }

                        }
                        //IsIgnore
                        var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (isIgnoreFilter != null)
                        {
                            var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                            if (canParse/* && isIgnore*/)
                            {
                                lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                            }
                        }
                        //NumOfRound
                        var isNumOfRoundFilter = request.Filters.Where(_ => _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (isNumOfRoundFilter != null)
                        {
                            var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                            if (canParse)
                            {
                                lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                            }
                        }
                        //QAStatus
                        var qAStautsFilter = request.Filters.Where(_ => _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                        if (qAStautsFilter != null)
                        {
                            var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                            if (canParse)
                            {
                                //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                                lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                                //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                            }
                        }
                    }
                    else
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                    }
                    //Apply thêm sort
                    if (request.Sorts != null && request.Sorts.Count > 0)
                    {
                        var isValidSort = false;
                        SortDefinition<Job> newSort = null;
                        foreach (var item in request.Sorts)
                        {
                            if (typeof(Job).GetProperty(item.Field) != null)
                            {
                                if (!isValidSort)
                                {
                                    newSort = item.OrderDirection == OrderDirection.Asc ?
                                       Builders<Job>.Sort.Ascending(item.Field)
                                       : Builders<Job>.Sort.Descending(item.Field);
                                }
                                else
                                {
                                    newSort = item.OrderDirection == OrderDirection.Asc ?
                                       newSort.Ascending(item.Field)
                                       : newSort.Descending(item.Field);
                                }


                                isValidSort = true;
                            }
                        }

                        if (isValidSort) baseOrder = newSort;
                    }
                    #endregion

                    #region Chuẩn bị dữ liệu
                    IEnumerable<SelectListItem> lstActionCode = new List<SelectListItem>();
                    var project = await _projectClientService.GetByInstanceIdAsync(Guid.Parse(projectInstanceId), accessToken);
                    if (project.Data.WorkflowInstanceId.HasValue && project.Data.WorkflowInstanceId != Guid.Empty)
                    {
                        var workflowDto = await _workflowClientService.GetByInstanceIdAsync(project.Data.WorkflowInstanceId.Value, accessToken);
                        var wfDto = workflowDto.Data ?? new WorkflowDto();

                        lstActionCode = wfDto?.LstWorkflowStepDto?.Select(x => new SelectListItem { Text = x.Name, Value = x.ActionCode });
                        if (workflowDto != null)
                        {
                            var wfData = await _workflowClientService.GetByInstanceIdAsync(workflowDto.Data.InstanceId, accessToken);
                        }
                    }
                    var lstUser = new List<UserDto>();
                    var lstUserInstanceId = new List<Guid>();
                    var lstUserJob = await _jobService.GetListUserInstanceIdByProject(Guid.Parse(projectInstanceId));
                    if (lstUserJob.Success && lstUserJob.Data.Any())
                    {
                        lstUserInstanceId = lstUserJob.Data;
                    }
                    if (lstUserInstanceId.Any())
                    {
                        var lstUserResponse = await _appUserClientService.GetUserInfoes(JsonConvert.SerializeObject(lstUserInstanceId), accessToken);
                        if (lstUserResponse.Success && lstUserResponse.Data.Any())
                        {
                            lstUser = lstUserResponse.Data.ToList();
                        }
                    }
                    #endregion

                    var findOptions = new FindOptions<Job> { BatchSize = 1000 };
                    findOptions.Projection = Builders<Job>.Projection //Chỉ lấy các trường cần thiết
                        .Include(x => x.Code)
                        .Include(x => x.DocPath)
                        .Include(x => x.DocName)
                        .Include(x => x.DocTypeFieldName)
                        .Include(x => x.ActionCode)
                        .Include(x => x.UserInstanceId)
                        .Include(x => x.ReceivedDate)
                        .Include(x => x.DueDate)
                        .Include(x => x.Status)
                        .Include(x => x.QaStatus)
                        .Include(x => x.NumOfRound)
                        .Include(x => x.Price)
                        .Include(x => x.Note)
                        .Include(x => x.Value)
                        .Include(x => x.IsIgnore)
                        .Include(x => x.RightStatus)
                        .Include(x => x.HasChange);
                    var cursor = await _jobRepository.GetCursorListJobAsync(lastFilter, findOptions);
                    var resultDocPathName = await _docClientService.GetListPath(project.Data.Id, accessToken);
                    var lstDocPathName = new List<DocPathDto>();
                    if (resultDocPathName != null && resultDocPathName.Data.Count() > 0)
                    {
                        lstDocPathName = resultDocPathName.Data;
                    }
                    var data = new List<Dictionary<string, object>>();
                    while (await cursor.MoveNextAsync())
                    {
                        var currentBatch = cursor.Current;
                        foreach (var job in currentBatch)
                        {
                            try
                            {
                                #region Chuẩn bị dữ liệu
                                var status = "";
                                if (job.ActionCode == nameof(ActionCodeConstants.DataEntry))
                                {
                                    if (job.IsIgnore)
                                    {
                                        status = "Bỏ qua";
                                    }
                                    else if (job.RightStatus == (int)EnumJob.RightStatus.WaitingConfirm)
                                    {
                                        status = "Chờ xác nhận";
                                    }
                                    else if (job.RightStatus == (int)EnumJob.RightStatus.Correct)
                                    {
                                        status = "Đã xử lý đúng";
                                    }
                                    else if (job.RightStatus == (int)EnumJob.RightStatus.Wrong)
                                    {
                                        status = "Đã xử lý sai";
                                    }
                                    else
                                    {
                                        status = "Chưa xác nhận";
                                    }
                                }
                                else if (job.ActionCode == nameof(ActionCodeConstants.DataCheck))
                                {
                                    if (job.IsIgnore)
                                    {
                                        status = "Bỏ qua";
                                    }
                                    else if (job.Status == (int)EnumJob.Status.Complete)

                                    {
                                        status = "Đã xử lý";
                                    }
                                    else
                                    {
                                        status = "Chưa xác nhận";
                                    }
                                }
                                else
                                {
                                    if (job.Status == (int)EnumJob.Status.Complete)
                                    {
                                        status = "Đã xử lý";
                                    }
                                    else
                                    {
                                        status = "Chưa xác nhận";
                                    }
                                }
                                var statusQA = "";
                                if (job.ActionCode == nameof(ActionCodeConstants.QACheckFinal))
                                {
                                    if (job.QaStatus == true)
                                    {
                                        statusQA = "Pass";
                                    }
                                    else if (job.QaStatus == false)
                                    {
                                        statusQA = "Fail";
                                    }
                                }
                                var pathName = lstDocPathName.FirstOrDefault(x => x.SyncMetaIdPath == job.DocPath);
                                var pathNameValue = pathName != null ? pathName.SyncMetaValuePath : string.Empty;
                                var wfStepName = lstActionCode.Where(x => x.Value == job.ActionCode).FirstOrDefault()?.Text;
                                string price = "0";
                                if (job.RightStatus == (int)EnumJob.RightStatus.Wrong || job.IsIgnore)
                                {
                                    price = "0";
                                }
                                else
                                {
                                    price = String.Format(CultureInfo.InvariantCulture, "{0:N}", job.Price);
                                }
                                var userFullName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.FullName;
                                var userName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.UserName;
                                var start = job.ReceivedDate.HasValue ? job.ReceivedDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                                var end = job.DueDate.HasValue ? job.DueDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                                var value = string.Empty;
                                if (job.ActionCode == ActionCodeConstants.SegmentLabeling || job.ActionCode == ActionCodeConstants.DataEntry || job.ActionCode == ActionCodeConstants.CheckFinal || job.ActionCode == ActionCodeConstants.QACheckFinal)
                                {
                                    value = job.DocName;
                                }
                                else value = job.Value;
                                #endregion
                                var row = new Dictionary<string, object>
                                {
                                    ["Mã công việc"] = GetSafeValue(job.Code),
                                    ["Đường dẫn"] = GetSafeValue(pathNameValue),
                                    ["Nội dung"] = GetSafeValue(value),
                                    ["Tên trường"] = GetSafeValue(job.DocTypeFieldName),
                                    ["Loại công việc"] = GetSafeValue(wfStepName),
                                    ["Nhân sự"] = GetSafeValue(userFullName + " - " + userName),
                                    ["Thời gian nhận dữ liệu"] = GetSafeValue(start),
                                    ["Thời gian hoàn thành dữ liệu"] = GetSafeValue(end),
                                    ["Trạng thái"] = GetSafeValue(status),
                                    ["Kết quả QA"] = GetSafeValue(statusQA),
                                    ["Số lần bị QA trả lại"] = job.NumOfRound.ToString(),
                                    ["Điểm thanh toán"] = GetSafeValue(price),
                                    ["Lý do trả lại"] = GetSafeValue(job.Note),
                                };
                                data.Add(row);
                                currentBatchSize++;
                                currentRecord++;
                                if (currentBatchSize >= maxBatchSize) // Nếu đủ số lượng bản ghi tối đa trên 1 file excel thì ghi vào file luôn và tạo file mới
                                {
                                    using (var fileStream = new FileStream(currentFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                                    {
                                        await MiniExcel.SaveAsAsync(fileStream, data);
                                        fileStream.Dispose();
                                    }
                                    currentBatchSize = 0;

                                    //Tạo file mới
                                    fileNumber++;
                                    currentFilePath = Path.Combine(tempDirectory, $"file_{fileNumber}.xlsx");

                                    // Cập nhật tiến trình
                                    exportData.Progress = (decimal)currentRecord / (decimal)totalRecord;
                                    await _exportDataClientService.UpdateAsync(exportData, accessToken);

                                    data.Clear();
                                }
                            }
                            catch (Exception ex)
                            {
                                isError = true;
                                if (isError)
                                {
                                    exportData.Status = (short)EnumExportData.Status.Error;
                                    exportData.Note = ex.Message;
                                    await _exportDataClientService.UpdateAsync(exportData, accessToken);
                                }
                                Log.Debug(ex.Message);
                                throw new Exception(ex.Message);
                            }
                        }
                        if (data.Count() < maxBatchSize) // Trường hợp currentBatch nhỏ hơn batchSize bản ghi thì ghi vào file excel luôn
                        {
                            // Cập nhật tiến trình
                            exportData.Progress = (decimal)currentRecord / (decimal)totalRecord;
                            await _exportDataClientService.UpdateAsync(exportData, accessToken);

                            //using (var fileStream = new FileStream(currentFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                            //{
                            //    await MiniExcel.SaveAsAsync(fileStream, data);
                            //    fileStream.Dispose();
                            //}
                            //data.Clear();
                        }
                    }
                    if (data.Count() < maxBatchSize) // Trường hợp dữ liệu nhỏ hơn batchSize bản ghi
                    {
                        // Cập nhật tiến trình
                        exportData.Progress = (decimal)currentRecord / (decimal)totalRecord;
                        await _exportDataClientService.UpdateAsync(exportData, accessToken);

                        using (var fileStream = new FileStream(currentFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                        {
                            await MiniExcel.SaveAsAsync(fileStream, data);
                            fileStream.Dispose();
                        }
                        data.Clear();
                    }
                    if (lstDocPathName != null)
                    {
                        lstDocPathName.Clear();
                    }
                    if (File.Exists(zipFilePath))
                    {
                        File.Delete(zipFilePath);
                    }
                    ZipFile.CreateFromDirectory(tempDirectory, zipFilePath);
                    //result = await File.ReadAllBytesAsync(zipFilePath);

                    if (!File.Exists(zipFilePath)) // Trường hợp file zip không tồn tại
                    {
                        // Cố gắng taọ lại file zip
                        ZipFile.CreateFromDirectory(tempDirectory, zipFilePath);
                    }
                    else // Trường hợp file zip đã được tạo
                    {
                        var fileName = Path.GetFileName(zipFilePath);
                        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(zipFilePath);
                        var streamFileZip = new FileStream(zipFilePath, FileMode.Open, FileAccess.Read);
                        result = new FormFile(streamFileZip, 0, streamFileZip.Length, fileNameWithoutExt, fileName)
                        {
                            Headers = new HeaderDictionary(),
                            ContentType = "application/octet-stream"
                        };
                        streamFileZip.Close();
                    }
                    if (result != null && result.Length > 0)
                    {
                        var fileUpload = new StgFileDto
                        {
                            Name = zipFileName,
                            File = result,
                            Size = result.Length,
                            //FileType = "application/zip",
                            FileExtension = ".zip",
                        };
                        //Upload file zip lên storage
                        var uploadResult = await _stgFileClientService.Upload(fileUpload, accessToken);
                        if (uploadResult != null && uploadResult.Success && uploadResult.Data != null)
                        {
                            //Lấy đường dẫn file zip
                            var filePath = uploadResult.Data.FilePath;

                            //Update link đường dẫn về ExportData
                            exportData.Progress = 1;
                            exportData.FilePath = filePath;
                            exportData.Size = result.Length;
                            exportData.LastModificationDate = DateTime.UtcNow;
                            exportData.LastModifiedBy = exportData.CreatedBy;
                            if (isError)
                            {
                                exportData.Status = (short)EnumExportData.Status.Error;
                            }
                            else
                            {
                                exportData.Status = (short)EnumExportData.Status.Complete;
                            }
                            await _exportDataClientService.UpdateAsync(exportData, accessToken);
                            Log.Information($"ExportExcelHistoryJob: End!");
                        }
                    }
                }
                catch (Exception ex)
                {
                    isError = true;
                    if (isError)
                    {
                        exportData.Status = (short)EnumExportData.Status.Error;
                        exportData.Note = ex.Message;
                        await _exportDataClientService.UpdateAsync(exportData, accessToken);
                    }
                    Log.Error("ERR: Lỗi khi xử lý kết xuất lịch sử xử lý dữ liệu ex:" + ex.Message);
                    throw new Exception(ex.Message);
                }
                finally
                {
                    //Xóa thư mục temp
                    //Directory.Delete(tempDirectory, true);
                    try
                    {
                        if (File.Exists(zipFilePath))
                        {
                            File.Delete(zipFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error("ERR_ExportExcelHistoryJob: Lỗi xóa file temp ex:" + ex.Message);
                    }
                }
            }
        }
        /// <summary>
        /// Hàm xuất dữ liệu ra file excel. 
        /// Có bao nhiêu dữ liệu xuất hết vào một file excel
        /// </summary>
        /// <param name="request"></param>
        /// <param name="actionCode"></param>
        /// <param name="accessToken"></param>
        /// <returns></returns>
        public async Task<byte[]> ExportExcelHistoryJobByUser(PagingRequest request, string actionCode, string accessToken)
        {
            byte[] result;
            var tempFilePath = Path.GetTempFileName();
            try
            {
                var allData = new List<Dictionary<string, object>>();
                string projectInstanceId = "";
                #region filter & short
                string statusFilterValue = "";
                string codeFilterValue = "";
                string pathFilterValue = "";
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //DocPath
                    var pathFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocPath)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (pathFilter != null)
                    {
                        pathFilterValue = pathFilter.Value.Trim();
                    }
                    //Status
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Status)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        statusFilterValue = statusFilter.Value.Trim();
                    }

                    //Code
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        codeFilterValue = codeFilter.Value.Trim();
                    }
                }
                // Nếu không có Status truyền vào thì mặc định Status là Complete
                var baseFilter = Builders<Job>.Filter.Eq(x => x.Status, statusFilterValue == "" ? (short)EnumJob.Status.Complete : short.Parse(statusFilterValue));
                if (!string.IsNullOrEmpty(codeFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.Code, codeFilterValue);
                }
                //Lấy ra các việc có DocPath bắt đầu bằng path được truyền vào nếu có
                if (!string.IsNullOrEmpty(pathFilterValue))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Regex(x => x.DocPath, new MongoDB.Bson.BsonRegularExpression($"^{pathFilterValue}"));
                }
                if (!string.IsNullOrEmpty(actionCode))
                {
                    baseFilter = baseFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, actionCode);
                }

                var baseOrder = Builders<Job>.Sort.Descending(nameof(Job.LastModificationDate));

                if (_userPrincipalService == null)
                {
                    return null;
                }

                var lastFilter = baseFilter;

                //Apply thêm filter
                if (request.Filters != null && request.Filters.Count > 0)
                {
                    //StartDate
                    var startDateFilter = request.Filters.Where(_ => _.Field.Equals("StartDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (startDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(startDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime startDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Gte(x => x.LastModificationDate, startDate.ToUniversalTime());
                    }

                    //endDate
                    var endDateFilter = request.Filters.Where(_ => _.Field.Equals("EndDate") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (endDateFilter != null)
                    {
                        var canParse = DateTime.TryParse(endDateFilter.Value, CultureInfo.CreateSpecificCulture("vi-vn"), DateTimeStyles.AssumeLocal, out DateTime endDate);
                        if (canParse) lastFilter = lastFilter & Builders<Job>.Filter.Lt(x => x.LastModificationDate, endDate.ToUniversalTime());
                    }

                    //DocInstanceId
                    var docInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.DocInstanceId, Guid.Parse(docInstanceIdFilter.Value));
                    }


                    //DocName
                    var docNameFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.DocName)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (docNameFilter != null)
                    {
                        if (docNameFilter.Value.Trim().ToUpper().Contains('J'))
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim().ToUpper()));
                        }
                        else lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.DocName, new MongoDB.Bson.BsonRegularExpression(docNameFilter.Value.Trim()));
                    }

                    //JobCode
                    var codeFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.Code)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (codeFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Regex(x => x.Code, new MongoDB.Bson.BsonRegularExpression(codeFilter.Value.Trim()));
                    }

                    //NormalState
                    var normalState = request.Filters.Where(_ => _.Field.Equals("NormalState") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (normalState != null)
                    {
                        var canParse = Int32.TryParse(normalState.Value, out int stateValue);
                        if (canParse)
                        {
                            switch (actionCode)
                            {
                                case ActionCodeConstants.DataEntry:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, false);
                                    }
                                    break;
                                case ActionCodeConstants.DataCheck:
                                case ActionCodeConstants.CheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                case ActionCodeConstants.QACheckFinal:
                                    if (stateValue == 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, true);
                                    }
                                    if (stateValue > 0)
                                    {
                                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.HasChange, false);
                                    }
                                    break;
                                default:
                                    break;
                            }
                        }
                    }

                    //ProjectInstanceId
                    var projectInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals(nameof(JobDto.ProjectInstanceId)) && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (projectInstanceIdFilter != null)
                    {
                        projectInstanceId = projectInstanceIdFilter.Value;
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ProjectInstanceId, Guid.Parse(projectInstanceIdFilter.Value));
                    }

                    //UserInstanceId
                    var userInstanceIdFilter = request.Filters.Where(_ => _.Field.Equals("UserInstanceId") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (userInstanceIdFilter != null)
                    {
                        lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, Guid.Parse(userInstanceIdFilter.Value));
                    }
                    else
                    {
                        if (projectInstanceIdFilter == null)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                        }
                        else
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Ne(x => x.UserInstanceId, null);
                        }
                    }

                    //RightStatus =>//EnumJob.RightStatus
                    var statusFilter = request.Filters.Where(_ => _.Field.Equals("RightStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (statusFilter != null)
                    {
                        var canParse = Int16.TryParse(statusFilter.Value, out short statusValue);

                        if (canParse && statusValue >= 0)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.RightStatus, statusValue);
                        }

                    }
                    //IsIgnore
                    var isIgnoreFilter = request.Filters.Where(_ => _.Field.Equals("IsIgnore") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isIgnoreFilter != null)
                    {
                        var canParse = Boolean.TryParse(isIgnoreFilter.Value, out bool isIgnore);

                        if (canParse/* && isIgnore*/)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.IsIgnore, isIgnore);
                        }
                    }
                    //NumOfRound
                    var isNumOfRoundFilter = request.Filters.Where(_ => _.Field.Equals("NumOfRound") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (isNumOfRoundFilter != null)
                    {
                        var canParse = Int16.TryParse(isNumOfRoundFilter.Value, out short numOfRound);

                        if (canParse)
                        {
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.NumOfRound, numOfRound);
                        }
                    }
                    //QAStatus
                    var qAStautsFilter = request.Filters.Where(_ => _.Field.Equals("QAStatus") && !string.IsNullOrWhiteSpace(_.Value)).FirstOrDefault();
                    if (qAStautsFilter != null)
                    {
                        var canParse = Boolean.TryParse(qAStautsFilter.Value, out bool qAStatus);

                        if (canParse)
                        {
                            //Nếu lọc theo QAStatus thì chỉ lấy theo bước QACheckFinal
                            lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.QaStatus, qAStatus);
                            //lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.ActionCode, ActionCodeConstants.QACheckFinal);
                        }
                    }
                }
                else
                {
                    lastFilter = lastFilter & Builders<Job>.Filter.Eq(x => x.UserInstanceId, _userPrincipalService.UserInstanceId.GetValueOrDefault());
                }
                //Apply thêm sort
                if (request.Sorts != null && request.Sorts.Count > 0)
                {
                    var isValidSort = false;
                    SortDefinition<Job> newSort = null;
                    foreach (var item in request.Sorts)
                    {
                        if (typeof(Job).GetProperty(item.Field) != null)
                        {
                            if (!isValidSort)
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   Builders<Job>.Sort.Ascending(item.Field)
                                   : Builders<Job>.Sort.Descending(item.Field);
                            }
                            else
                            {
                                newSort = item.OrderDirection == OrderDirection.Asc ?
                                   newSort.Ascending(item.Field)
                                   : newSort.Descending(item.Field);
                            }


                            isValidSort = true;
                        }
                    }

                    if (isValidSort) baseOrder = newSort;
                }
                #endregion

                IEnumerable<SelectListItem> lstActionCode = new List<SelectListItem>();
                var project = await _projectClientService.GetByInstanceIdAsync(Guid.Parse(projectInstanceId), accessToken);
                if (project.Data.WorkflowInstanceId.HasValue && project.Data.WorkflowInstanceId != Guid.Empty)
                {
                    var workflowDto = await _workflowClientService.GetByInstanceIdAsync(project.Data.WorkflowInstanceId.Value, accessToken);
                    var wfDto = workflowDto.Data ?? new WorkflowDto();

                    lstActionCode = wfDto?.LstWorkflowStepDto?.Select(x => new SelectListItem { Text = x.Name, Value = x.ActionCode });
                    if (workflowDto != null)
                    {
                        var wfData = await _workflowClientService.GetByInstanceIdAsync(workflowDto.Data.InstanceId, accessToken);
                    }
                }
                var lstUser = new List<UserDto>();
                var lstUserInstanceId = new List<Guid>();
                var lstUserJob = await _jobService.GetListUserInstanceIdByProject(Guid.Parse(projectInstanceId));
                if (lstUserJob.Success && lstUserJob.Data.Any())
                {
                    lstUserInstanceId = lstUserJob.Data;
                }
                if (lstUserInstanceId.Any())
                {
                    var lstUserResponse = await _appUserClientService.GetUserInfoes(JsonConvert.SerializeObject(lstUserInstanceId), accessToken);
                    if (lstUserResponse.Success && lstUserResponse.Data.Any())
                    {
                        lstUser = lstUserResponse.Data.ToList();
                    }
                }

                long totalRow = 0;

                var findOptions = new FindOptions<Job> { BatchSize = 1000 };
                var cursor = await _jobRepository.GetCursorListJobAsync(lastFilter, findOptions);
                using (var fileStream = new FileStream(tempFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    var data = new List<Dictionary<string, object>>();
                    while (await cursor.MoveNextAsync())
                    {
                        var currentBatch = cursor.Current;

                        totalRow += currentBatch.Count();
                        var lstDocPathName = new Dictionary<string, string>();
                        var lstDocPath = currentBatch.Select(x => x.DocPath).ToList().Distinct();
                        var requestLstDocPath = JsonConvert.SerializeObject(lstDocPath);
                        var resultDocPathName = await _docClientService.GetMultiPathNameByMultiDocPath(requestLstDocPath, accessToken);
                        if (resultDocPathName != null)
                        {
                            lstDocPathName = resultDocPathName.Data;
                        }
                        foreach (var job in currentBatch)
                        {
                            #region Status,QAStatus
                            var status = "";
                            if (job.ActionCode == nameof(ActionCodeConstants.DataEntry))
                            {
                                if (job.IsIgnore)
                                {
                                    status = "Bỏ qua";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.WaitingConfirm)
                                {
                                    status = "Chờ xác nhận";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.Correct)
                                {
                                    status = "Đã xử lý đúng";
                                }
                                else if (job.RightStatus == (int)EnumJob.RightStatus.Wrong)
                                {
                                    status = "Đã xử lý sai";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            else if (job.ActionCode == nameof(ActionCodeConstants.DataCheck))
                            {
                                if (job.IsIgnore)
                                {
                                    status = "Bỏ qua";
                                }
                                else if (job.Status == (int)EnumJob.Status.Complete)

                                {
                                    status = "Đã xử lý";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            else
                            {
                                if (job.Status == (int)EnumJob.Status.Complete)
                                {
                                    status = "Đã xử lý";
                                }
                                else
                                {
                                    status = "Chưa xác nhận";
                                }
                            }
                            var statusQA = "";
                            if (job.ActionCode == nameof(ActionCodeConstants.QACheckFinal))
                            {
                                if (job.QaStatus == true)
                                {
                                    statusQA = "Pass";
                                }
                                else if (job.QaStatus == false)
                                {
                                    statusQA = "Fail";
                                }
                            }
                            #endregion
                            #region pathName
                            var pathName = lstDocPathName.GetValueOrDefault(job.DocPath) + "/" + job.DocName ?? "";
                            #endregion
                            var wfStepName = lstActionCode.Where(x => x.Value == job.ActionCode).FirstOrDefault()?.Text;
                            string price = "0";
                            if (job.RightStatus == (int)EnumJob.RightStatus.Wrong || job.IsIgnore)
                            {
                                price = "0";
                            }
                            else
                            {
                                price = String.Format(CultureInfo.InvariantCulture, "{0:N}", job.Price);
                            }
                            var userFullName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.FullName;
                            var userName = lstUser.Where(x => x.InstanceId == job.UserInstanceId).FirstOrDefault()?.UserName;
                            var start = job.ReceivedDate.HasValue ? job.ReceivedDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                            var end = job.DueDate.HasValue ? job.DueDate.Value.ToLocalTime().ToString("dd-MM-yyyy hh:mm:ss") : "";
                            var value = string.Empty;
                            if (job.ActionCode == ActionCodeConstants.SegmentLabeling || job.ActionCode == ActionCodeConstants.DataEntry || job.ActionCode == ActionCodeConstants.CheckFinal || job.ActionCode == ActionCodeConstants.QACheckFinal)
                            {
                                value = job.DocName;
                            }
                            else value = job.Value;
                            var row = new Dictionary<string, object>
                            {
                                ["Mã công việc"] = GetSafeValue(job.Code),
                                ["Đường dẫn"] = GetSafeValue(pathName),
                                ["Nội dung"] = GetSafeValue(value),
                                ["Loại công việc"] = GetSafeValue(wfStepName),
                                ["Nhân sự"] = GetSafeValue(userFullName + " - " + userName),
                                ["Thời gian nhận dữ liệu"] = GetSafeValue(start),
                                ["Thời gian hoàn thành dữ liệu"] = GetSafeValue(end),
                                ["Trạng thái"] = GetSafeValue(status),
                                ["Kết quả QA"] = GetSafeValue(statusQA),
                                ["Số lần bị QA trả lại"] = job.NumOfRound.ToString(),
                                ["Điểm thanh toán"] = GetSafeValue(price),
                                ["Lý do trả lại"] = GetSafeValue(job.Note),
                            };
                            allData.Add(row);
                            //Log.Debug("Row:" + JsonConvert.SerializeObject(row));
                        }

                        //SaveAsFile(fileStream, data);
                        //fileStream.SaveAs(data);
                        Log.Debug("ExportExcelTotalRow:" + totalRow);
                        lstDocPathName.Clear();
                        data.Clear();
                        //GC.Collect();

                    }
                    Log.Debug("ExportExcelTotalRowAll:" + totalRow);
                    SaveAsFile(fileStream, allData);
                    allData.Clear();
                    allData = null;
                    GC.Collect(); //Thu hồi lại memory
                }
                result = await File.ReadAllBytesAsync(tempFilePath);
            }
            finally
            {
                // Xóa file tạm thời
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            return result;
        }

        void SaveAsFile(FileStream fileStream, List<Dictionary<string, object>> data)
        {
            try
            {
                fileStream.SaveAs(data);
                fileStream.Flush();

                Log.Debug($"Data saved to file. Current file size: {fileStream.Length}");
            }
            catch (Exception ex)
            {
                Log.Error($"Error while saving data to file: {ex.Message}");
            }
        }
        private string GetSafeValue(object value)
        {
            return value != null ? value.ToString() : "";
        }
    }
}
