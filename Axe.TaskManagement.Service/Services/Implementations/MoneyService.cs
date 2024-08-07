using Axe.TaskManagement.Data.Repositories.Interfaces;
using Axe.TaskManagement.Model.Entities;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.Definitions;
using Axe.Utility.Dtos;
using Axe.Utility.EntityExtensions;
using Axe.Utility.Enums;
using Axe.Utility.Helpers;
using Axe.Utility.MessageTemplate;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class MoneyService : IMoneyService
    {
        private readonly IJobRepository _repository;
        private readonly ITransactionClientService _transactionClientService;
        private readonly IUserProjectClientService _userProjectClientService;
        private const string MsgComplainJobInfo = "Khiếu nại {0} công việc {1}";  // TODO: Move prj Axe.Utility
        private const string DescriptionTranferMoneyForComplainJob = "Chuyển tiền từ ví của tài khoản {0} đến ví của tài khoản {1} do khiếu nại công việc {2}"; // TODO: Move prj Axe.Utility

        public MoneyService(IJobRepository repository
            , ITransactionClientService transactionClientService
            , IUserProjectClientService userProjectClientService)
        {
            _repository = repository;
            _transactionClientService = transactionClientService;
            _userProjectClientService = userProjectClientService;
        }

        public async Task ChargeMoneyForCompleteDoc(List<WorkflowStepInfo> wfsInfoes,
            List<WorkflowSchemaConditionInfo> wfSchemaInfoes, List<DocItem> docItems, Guid docInstanceId,
            string accessToken)
        {
            var allJobs = await _repository.GetJobByWfs(docInstanceId);
            // Chỉ lấy những jobs manual
            var allManualJobs = allJobs
                .Where(x => wfsInfoes.Any(w => !w.IsAuto && w.InstanceId == x.WorkflowStepInstanceId)).ToList();
            var completeJobCodes = allManualJobs.Select(x => x.Code);
            var projectInstanceId = allManualJobs.FirstOrDefault()?.ProjectInstanceId;
            var clientInstanceId =
                await GetClientInstanceIdByProject(projectInstanceId.GetValueOrDefault(), accessToken);
            if (clientInstanceId != Guid.Empty)
            {
                var itemTransactionAdds = new List<ItemTransactionAddDto>();
                var updateJobs = new List<Job>();

                //Check tính đúng/sai
                foreach (var itemJob in allManualJobs)
                {
                    decimal changeAmount = 0;
                    var itemWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == itemJob.WorkflowStepInstanceId);
                    var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes,
                        itemJob.WorkflowStepInstanceId.GetValueOrDefault());
                    var prevWfsInfo = prevWfsInfoes.FirstOrDefault();
                    short rightStatus = (short)EnumJob.RightStatus.WaitingConfirm;
                    if (!string.IsNullOrEmpty(itemWfsInfo.ConfigPrice))
                    {
                        var objConfigPrice = JsonConvert.DeserializeObject<ConfigPriceV2>(itemWfsInfo.ConfigPrice);
                        if (objConfigPrice != null)
                        {
                            var itemTransactionAdd = new ItemTransactionAddDto
                            {
                                SourceUserInstanceId = clientInstanceId,
                                DestinationUserInstanceId = itemJob.UserInstanceId.GetValueOrDefault(),
                                //ChangeAmount = itemJob.Price,
                                ChangeProvisionalAmount = -itemJob.Price,
                                JobCode = itemJob.Code,
                                ProjectInstanceId = itemJob.ProjectInstanceId,
                                WorkflowInstanceId = itemJob.WorkflowInstanceId,
                                WorkflowStepInstanceId = itemJob.WorkflowStepInstanceId,
                                ActionCode = itemJob.ActionCode,
                                Message = string.Format(MsgTransactionTemplate.MsgJobInfoes, itemWfsInfo?.Name,
                                    itemJob.Code),
                                Description = string.Format(
                                    DescriptionTransactionTemplateV2.DescriptionTranferMoneyForCompleteJob,
                                    clientInstanceId,
                                    itemJob.UserInstanceId.GetValueOrDefault(),
                                    itemJob.Code)
                            };
                            if (!itemJob.IsIgnore)
                            {
                                if (itemWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                {
                                    var finalValItem = docItems.FirstOrDefault(x =>
                                        x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                    var isCorrect = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemJob.Value,
                                        finalValItem);
                                    if (isCorrect)
                                    {
                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                        {
                                            var preVal = allJobs.FirstOrDefault(x =>
                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                            var isPriceEdit = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preVal,
                                                itemJob.Value);
                                            changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                                itemJob.DigitizedTemplateInstanceId, itemJob.DocTypeFieldInstanceId,
                                                isPriceEdit);
                                            itemTransactionAdd.ChangeAmount = changeAmount;
                                            rightStatus = (short)EnumJob.RightStatus.Correct;
                                        }
                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                        {
                                            var preVals = allJobs.FirstOrDefault(x =>
                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                            var preDocItems = preVals != null && !string.IsNullOrEmpty(preVals.Value)
                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preVals.Value)
                                                : new List<DocItem>();
                                            var preVal = preDocItems.FirstOrDefault(x =>
                                                x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                            var isPriceEdit = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preVal,
                                                itemJob.Value);
                                            changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                                itemJob.DigitizedTemplateInstanceId, itemJob.DocTypeFieldInstanceId,
                                                isPriceEdit);
                                            itemTransactionAdd.ChangeAmount = changeAmount;
                                            rightStatus = (short)EnumJob.RightStatus.Correct;
                                        }
                                    }
                                    else
                                    {
                                        itemTransactionAdd.ChangeAmount = 0; // changeAmount = 0;
                                        rightStatus = (short)EnumJob.RightStatus.Wrong;
                                    }

                                    itemTransactionAdds.Add(itemTransactionAdd);
                                }
                                else
                                {
                                    var itemVals = JsonConvert.DeserializeObject<List<DocItem>>(itemJob.Value);
                                    if (itemVals != null && itemVals.Any())
                                    {
                                        if (objConfigPrice.Status ==
                                            (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                                        {
                                            var isCorrectTotal = true;
                                            var isPriceEditTotal = true;
                                            foreach (var itemVal in itemVals)
                                            {
                                                var finalValField = docItems.FirstOrDefault(x =>
                                                    x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                if (finalValField != null)
                                                {
                                                    var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode,
                                                        itemVal.Value, finalValField.Value);
                                                    if (isCorrectField)
                                                    {
                                                        if (prevWfsInfo?.Attribute ==
                                                            (short)EnumWorkflowStep.AttributeType.Meta)
                                                        {
                                                            var preValField = allJobs.FirstOrDefault(x =>
                                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            var isPriceEditField =
                                                                MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode,
                                                                    preValField?.Value, itemVal.Value);
                                                            if (!isPriceEditField)
                                                            {
                                                                isPriceEditTotal = false;
                                                            }
                                                        }
                                                        else if (prevWfsInfo?.Attribute ==
                                                                 (short)EnumWorkflowStep.AttributeType.File)
                                                        {
                                                            var preValFields = allJobs.FirstOrDefault(x =>
                                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                            var preDocItems = preValFields != null &&
                                                                              !string.IsNullOrEmpty(preValFields.Value)
                                                                ? JsonConvert.DeserializeObject<List<DocItem>>(
                                                                    preValFields.Value)
                                                                : new List<DocItem>();
                                                            var preValField = preDocItems.FirstOrDefault(x =>
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            var isPriceEditField =
                                                                MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode,
                                                                    preValField?.Value, itemVal.Value);
                                                            if (!isPriceEditField)
                                                            {
                                                                isPriceEditTotal = false;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        isCorrectTotal = false;
                                                        break;
                                                    }
                                                }
                                            }

                                            if (isCorrectTotal)
                                            {
                                                changeAmount = MoneyHelper.GetPriceByConfigPriceV2(
                                                    itemWfsInfo.ConfigPrice, itemJob.DigitizedTemplateInstanceId, null,
                                                    isPriceEditTotal);
                                                rightStatus = (short)EnumJob.RightStatus.Correct;
                                            }
                                            else
                                            {
                                                changeAmount = 0;
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                            }

                                        }
                                        else if (objConfigPrice.Status ==
                                                 (short)EnumWorkflowStep.UnitPriceConfigType.ByField)
                                        {
                                            var totalCorrect = 0;
                                            foreach (var itemVal in itemVals)
                                            {
                                                var finalValField = docItems.FirstOrDefault(x =>
                                                    x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                if (finalValField != null)
                                                {
                                                    var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode,
                                                        itemVal.Value, finalValField.Value);
                                                    if (isCorrectField)
                                                    {
                                                        if (prevWfsInfo?.Attribute ==
                                                            (short)EnumWorkflowStep.AttributeType.Meta)
                                                        {
                                                            var preValField = allJobs.FirstOrDefault(x =>
                                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            var isPriceEditField =
                                                                MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode,
                                                                    preValField?.Value, itemVal.Value);
                                                            changeAmount +=
                                                                MoneyHelper.GetPriceByConfigPriceV2(
                                                                    itemWfsInfo.ConfigPrice,
                                                                    itemJob.DigitizedTemplateInstanceId,
                                                                    itemVal.DocTypeFieldInstanceId, isPriceEditField);
                                                            totalCorrect++;
                                                        }
                                                        else if (prevWfsInfo?.Attribute ==
                                                                 (short)EnumWorkflowStep.AttributeType.File)
                                                        {
                                                            var preValFields = allJobs.FirstOrDefault(x =>
                                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                            var preDocItems = preValFields != null &&
                                                                              !string.IsNullOrEmpty(preValFields.Value)
                                                                ? JsonConvert.DeserializeObject<List<DocItem>>(
                                                                    preValFields.Value)
                                                                : new List<DocItem>();
                                                            var preValField = preDocItems.FirstOrDefault(x =>
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            var isPriceEditField =
                                                                MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode,
                                                                    preValField?.Value, itemVal.Value);
                                                            changeAmount +=
                                                                MoneyHelper.GetPriceByConfigPriceV2(
                                                                    itemWfsInfo.ConfigPrice,
                                                                    itemJob.DigitizedTemplateInstanceId,
                                                                    itemVal.DocTypeFieldInstanceId, isPriceEditField);
                                                            totalCorrect++;
                                                        }
                                                    }
                                                }
                                            }

                                            // TODO: Bổ sung tỷ lệ sai
                                            if (totalCorrect == 0)
                                            {
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                            }
                                            else if (totalCorrect == itemVals.Count)
                                            {
                                                rightStatus = (short)EnumJob.RightStatus.Correct;
                                            }
                                            else
                                            {
                                                //rightStatus = (short)EnumJob.RightStatus.Confirmed;
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                            }
                                        }

                                        // CheckFinal bị giảm 50% tổng tiền nếu QA trả lại lần 2
                                        if (itemJob.ActionCode == ActionCodeConstants.CheckFinal)
                                        {
                                            var jobWithMaxNumOfRound = allJobs
                                                .Where(x => x.ActionCode == itemJob.ActionCode &&
                                                            x.WorkflowStepInstanceId == itemJob.WorkflowStepInstanceId)
                                                .OrderByDescending(o => o.NumOfRound).First();
                                            if (itemJob.NumOfRound < jobWithMaxNumOfRound.NumOfRound)
                                            {
                                                changeAmount =
                                                    0; // Các job CheckFinal không có NumOfRound là max thì không được tính tiền
                                            }
                                            else
                                            {
                                                if (itemJob.NumOfRound >= 2)
                                                {
                                                    changeAmount = Math.Round(changeAmount / 2, 2);
                                                }
                                            }
                                        }

                                        itemTransactionAdd.ChangeAmount = changeAmount;
                                        itemTransactionAdds.Add(itemTransactionAdd);
                                    }
                                }
                            }

                            // Update RightStatus & Price
                            var tempJob = (Job)itemJob.Clone();
                            tempJob.Price = changeAmount;
                            tempJob.RightStatus = rightStatus;
                            updateJobs.Add(tempJob);
                        }
                    }
                }

                if (itemTransactionAdds.Any())
                {
                    var transactionAddMultiCompleteDoc = new TransactionAddMultiDto
                    {
                        CorrelationMessage = string.Format(MsgTransactionTemplate.MsgCompleteJobInfoes, docInstanceId,
                            string.Join(", ", completeJobCodes)),
                        CorrelationDescription = $"Hoàn thành phiếu {docInstanceId}",
                        ItemTransactionAdds = itemTransactionAdds
                    };

                    await _transactionClientService.AddMultiTransactionAsync(transactionAddMultiCompleteDoc,
                        accessToken);
                }

                // Update RightStatus & Price
                await _repository.UpdateMultiAsync(updateJobs);
            }
            else
            {
                Log.Logger.Error(
                    $"Can not get ClientInstanceId from ProjectInstanceId: {projectInstanceId.GetValueOrDefault()}!");
            }
        }

        public async Task ChargeMoneyForComplainJob(List<WorkflowStepInfo> wfsInfoes,
            List<WorkflowSchemaConditionInfo> wfSchemaInfoes, List<DocItem> docItems, Guid docInstanceId,
            Job itemJob, string accessToken)
        {
            
            var allJobs = await _repository.GetJobByWfs(docInstanceId);
            var projectInstanceId = itemJob.ProjectInstanceId;
            var clientInstanceId = await GetClientInstanceIdByProject(projectInstanceId.GetValueOrDefault(), accessToken);
            if (clientInstanceId != Guid.Empty)
            {
                //Check tính đúng/sai
                decimal changeAmount = 0;
                var itemWfsInfo = wfsInfoes.FirstOrDefault(x => x.InstanceId == itemJob.WorkflowStepInstanceId);
                var prevWfsInfoes = WorkflowHelper.GetPreviousSteps(wfsInfoes, wfSchemaInfoes,
                    itemJob.WorkflowStepInstanceId.GetValueOrDefault());
                var prevWfsInfo = prevWfsInfoes.FirstOrDefault();
                short rightStatus = (short)EnumJob.RightStatus.WaitingConfirm;
                if (!string.IsNullOrEmpty(itemWfsInfo?.ConfigPrice))
                {
                    var objConfigPrice = JsonConvert.DeserializeObject<ConfigPriceV2>(itemWfsInfo.ConfigPrice);
                    if (objConfigPrice != null)
                    {
                        var transactionAdd = new TransactionAddDto
                        {
                            SourceUserInstanceId = clientInstanceId,
                            DestinationUserInstanceId = itemJob.UserInstanceId.GetValueOrDefault(),
                            //ChangeAmount = itemJob.Price,
                            ChangeProvisionalAmount = -itemJob.Price,
                            JobCode = itemJob.Code,
                            ProjectInstanceId = itemJob.ProjectInstanceId,
                            WorkflowInstanceId = itemJob.WorkflowInstanceId,
                            WorkflowStepInstanceId = itemJob.WorkflowStepInstanceId,
                            ActionCode = itemJob.ActionCode,
                            Message = string.Format(MsgComplainJobInfo, itemWfsInfo.Name, itemJob.Code),
                            Description = string.Format(
                                DescriptionTranferMoneyForComplainJob,
                                clientInstanceId,
                                itemJob.UserInstanceId.GetValueOrDefault(),
                                itemJob.Code)
                        };
                        if (!itemJob.IsIgnore)
                        {
                            //Nhập phân mảnh
                            if (itemWfsInfo.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                            {
                                var finalValItem = docItems.FirstOrDefault(x =>
                                    x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                var isCorrect = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemJob.Value,
                                    finalValItem);
                                if (isCorrect)
                                {
                                    if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                    {
                                        var preVal = allJobs.FirstOrDefault(x =>
                                            x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                            x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                        var isPriceEdit = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preVal,
                                            itemJob.Value);
                                        changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                            itemJob.DigitizedTemplateInstanceId, itemJob.DocTypeFieldInstanceId,
                                            isPriceEdit);
                                        transactionAdd.ChangeAmount = changeAmount;
                                        rightStatus = (short)EnumJob.RightStatus.Correct;
                                    }
                                    else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                    {
                                        var preVals = allJobs.FirstOrDefault(x =>
                                            x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                        var preDocItems = preVals != null && !string.IsNullOrEmpty(preVals.Value)
                                            ? JsonConvert.DeserializeObject<List<DocItem>>(preVals.Value)
                                            : new List<DocItem>();
                                        var preVal = preDocItems?.FirstOrDefault(x =>
                                            x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                        var isPriceEdit = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preVal,
                                            itemJob.Value);
                                        changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                            itemJob.DigitizedTemplateInstanceId, itemJob.DocTypeFieldInstanceId,
                                            isPriceEdit);
                                        transactionAdd.ChangeAmount = changeAmount;
                                        rightStatus = (short)EnumJob.RightStatus.Correct;
                                    }
                                }
                                else
                                {
                                    transactionAdd.ChangeAmount = 0; // changeAmount = 0;
                                    rightStatus = (short)EnumJob.RightStatus.Wrong;
                                }
                            }
                            //Nhập toàn phiếu
                            else
                            {
                                var itemVals = JsonConvert.DeserializeObject<List<DocItem>>(itemJob.Value);
                                if (itemVals != null && itemVals.Any())
                                {
                                    if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                                    {
                                        var isCorrectTotal = true;
                                        var isPriceEditTotal = true;
                                        foreach (var itemVal in itemVals)
                                        {
                                            var finalValField = docItems.FirstOrDefault(x =>
                                                x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                            if (finalValField != null)
                                            {
                                                var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode,
                                                    itemVal.Value, finalValField.Value);
                                                if (isCorrectField)
                                                {
                                                    if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                    {
                                                        var preValField = allJobs.FirstOrDefault(x =>
                                                            x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                            x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                        var isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
                                                        if (!isPriceEditField)
                                                        {
                                                            isPriceEditTotal = false;
                                                        }
                                                    }
                                                    else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                                    {
                                                        var preValFields = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                        var preDocItems = preValFields != null && !string.IsNullOrEmpty(preValFields.Value)
                                                            ? JsonConvert.DeserializeObject<List<DocItem>>(preValFields.Value)
                                                            : new List<DocItem>();
                                                        var preValField = preDocItems?.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                        var isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
                                                        if (!isPriceEditField)
                                                        {
                                                            isPriceEditTotal = false;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    isCorrectTotal = false;
                                                    break;
                                                }
                                            }
                                        }

                                        if (isCorrectTotal)
                                        {
                                            changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                                itemJob.DigitizedTemplateInstanceId, null, isPriceEditTotal);
                                            rightStatus = (short)EnumJob.RightStatus.Correct;
                                        }
                                        else
                                        {
                                            changeAmount = 0;
                                            rightStatus = (short)EnumJob.RightStatus.Wrong;
                                        }

                                    }
                                    else if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByField)
                                    {
                                        var totalCorrect = 0;
                                        foreach (var itemVal in itemVals)
                                        {
                                            var finalValField = docItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                            if (finalValField != null)
                                            {
                                                var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemVal.Value, finalValField.Value);
                                                if (isCorrectField)
                                                {
                                                    if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                    {
                                                        var preValField = allJobs.FirstOrDefault(x =>
                                                            x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                            x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                        var isPriceEditField =
                                                            MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode,
                                                                preValField?.Value, itemVal.Value);
                                                        changeAmount += MoneyHelper.GetPriceByConfigPriceV2(
                                                                itemWfsInfo.ConfigPrice,
                                                                itemJob.DigitizedTemplateInstanceId,
                                                                itemVal.DocTypeFieldInstanceId, isPriceEditField);
                                                        totalCorrect++;
                                                    }
                                                    else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                                    {
                                                        var preValFields = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                        var preDocItems = preValFields != null && !string.IsNullOrEmpty(preValFields.Value)
                                                            ? JsonConvert.DeserializeObject<List<DocItem>>(
                                                                preValFields.Value)
                                                            : new List<DocItem>();
                                                        var preValField = preDocItems?.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                        var isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
                                                        changeAmount += MoneyHelper.GetPriceByConfigPriceV2(
                                                                itemWfsInfo.ConfigPrice,
                                                                itemJob.DigitizedTemplateInstanceId,
                                                                itemVal.DocTypeFieldInstanceId, isPriceEditField);
                                                        totalCorrect++;
                                                    }
                                                }
                                            }
                                        }

                                        // TODO: Bổ sung tỷ lệ sai
                                        if (totalCorrect == 0)
                                        {
                                            rightStatus = (short)EnumJob.RightStatus.Wrong;
                                        }
                                        else if (totalCorrect == itemVals.Count)
                                        {
                                            rightStatus = (short)EnumJob.RightStatus.Correct;
                                        }
                                        else
                                        {
                                            //rightStatus = (short)EnumJob.RightStatus.Confirmed;
                                            rightStatus = (short)EnumJob.RightStatus.Wrong;
                                        }
                                    }

                                    // CheckFinal bị giảm 50% tổng tiền nếu QA trả lại lần 2
                                    if (itemJob.ActionCode == ActionCodeConstants.CheckFinal)
                                    {
                                        var jobWithMaxNumOfRound = allJobs.Where(x => x.ActionCode == itemJob.ActionCode && x.WorkflowStepInstanceId == itemJob.WorkflowStepInstanceId)
                                            .OrderByDescending(o => o.NumOfRound).First();
                                        if (itemJob.NumOfRound < jobWithMaxNumOfRound.NumOfRound)
                                        {
                                            changeAmount = 0; // Các job CheckFinal không có NumOfRound là max thì không được tính tiền
                                        }
                                        else
                                        {
                                            if (itemJob.NumOfRound >= 2)
                                            {
                                                changeAmount = Math.Round(changeAmount / 2, 2);
                                            }
                                        }
                                    }

                                    transactionAdd.ChangeAmount = changeAmount;
                                }
                            }
                        }

                        // Update RightStatus & Price
                        itemJob.Price = changeAmount;
                        itemJob.RightStatus = rightStatus;
                        await _repository.UpdateAsync(itemJob);

                        await _transactionClientService.AddTransactionAsync(transactionAdd, accessToken);
                    }
                }
            }
            else
            {
                Log.Logger.Error(
                    $"Can not get ClientInstanceId from ProjectInstanceId: {projectInstanceId.GetValueOrDefault()}!");
            }
        }

        #region Private methods

        private async Task<Guid> GetClientInstanceIdByProject(Guid projectInstanceId, string accessToken = null)
        {
            var clientInstanceIdsResult =
                await _userProjectClientService.GetPrimaryUserInstanceIdByProject(projectInstanceId, accessToken);
            if (clientInstanceIdsResult != null && clientInstanceIdsResult.Success)
            {
                return clientInstanceIdsResult.Data;
            }

            return Guid.Empty;
        }

        #endregion
    }
}
