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
        private const int LimitRoundFullMoney = 2;

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
                    short rightStatus = itemJob.RightStatus;
                    decimal rightRatio = itemJob.RightRatio;
                    string priceDetails = itemJob.PriceDetails;
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
                                    var finalValItem = docItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                    var isCorrect = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemJob.Value, finalValItem);
                                    if (isCorrect)
                                    {
                                        string preVal = null;
                                        bool isPriceEdit;
                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                        {
                                            preVal = allJobs.FirstOrDefault(x =>
                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                        }
                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                        {
                                            var preVals = allJobs.FirstOrDefault(x =>
                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                            var preDocItems = preVals != null && !string.IsNullOrEmpty(preVals.Value)
                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preVals.Value)
                                                : new List<DocItem>();
                                            preVal = preDocItems.FirstOrDefault(x =>
                                                x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                        }

                                        isPriceEdit = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preVal,
                                            itemJob.Value);
                                        changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                            itemJob.DigitizedTemplateInstanceId, itemJob.DocTypeFieldInstanceId,
                                            isPriceEdit);
                                        rightStatus = (short)EnumJob.RightStatus.Correct;
                                        rightRatio = 1;
                                    }
                                    else
                                    {
                                        changeAmount = 0;
                                        rightStatus = (short)EnumJob.RightStatus.Wrong;
                                        rightRatio = 0;
                                    }

                                    priceDetails = JsonConvert.SerializeObject(new List<PriceItem>
                                    {
                                        new PriceItem
                                        {
                                            DocTypeFieldInstanceId = itemJob.DocTypeFieldInstanceId,
                                            Price = changeAmount,
                                            RightStatus = rightStatus
                                        }
                                    });
                                    itemTransactionAdd.ChangeAmount = changeAmount;
                                    itemTransactionAdds.Add(itemTransactionAdd);
                                }
                                else
                                {
                                    var itemVals = JsonConvert.DeserializeObject<List<DocItem>>(itemJob.Value);
                                    if (itemVals != null && itemVals.Any())
                                    {
                                        var priceItems = new List<PriceItem>();
                                        if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                                        {
                                            var isCorrectTotal = true;
                                            var isPriceEditTotal = true;
                                            foreach (var itemVal in itemVals)
                                            {
                                                var finalValField = docItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                if (finalValField != null)
                                                {
                                                    var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemVal.Value, finalValField.Value);
                                                    if (isCorrectField)
                                                    {
                                                        Job preJob;
                                                        bool isPriceEditField;
                                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                                x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preJob?.Value, itemVal.Value);
                                                            if (!isPriceEditField)
                                                            {
                                                                isPriceEditTotal = false;
                                                            }
                                                        }
                                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x =>
                                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                            var preDocItems = preJob != null && !string.IsNullOrEmpty(preJob.Value)
                                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preJob.Value)
                                                                : new List<DocItem>();
                                                            var preValField = preDocItems.FirstOrDefault(x =>
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
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
                                                rightRatio = 1;
                                            }
                                            else
                                            {
                                                changeAmount = 0;
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                                rightRatio = 0;
                                            }

                                            priceItems.Add(new PriceItem
                                            {
                                                DocTypeFieldInstanceId = itemJob.DocTypeFieldInstanceId,    // itemJob.DocTypeFieldInstanceId = null
                                                Price = changeAmount,
                                                RightStatus = rightStatus
                                            });
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
                                                        Job preJob;
                                                        bool isPriceEditField = true;
                                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                                x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preJob?.Value, itemVal.Value);
                                                            
                                                        }
                                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                            var preDocItems = preJob != null && !string.IsNullOrEmpty(preJob.Value)
                                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preJob.Value)
                                                                : new List<DocItem>();
                                                            var preValField = preDocItems.FirstOrDefault(x =>
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
                                                        }

                                                        var changeAmountItem =
                                                            MoneyHelper.GetPriceByConfigPriceV2(
                                                                itemWfsInfo.ConfigPrice,
                                                                itemJob.DigitizedTemplateInstanceId,
                                                                itemVal.DocTypeFieldInstanceId, isPriceEditField);
                                                        changeAmount += changeAmountItem;
                                                        totalCorrect++;
                                                        priceItems.Add(new PriceItem
                                                        {
                                                            DocTypeFieldInstanceId = itemVal.DocTypeFieldInstanceId,
                                                            Price = changeAmountItem,
                                                            RightStatus = (short)EnumJob.RightStatus.Correct
                                                        });
                                                    }
                                                    else
                                                    {
                                                        priceItems.Add(new PriceItem
                                                        {
                                                            DocTypeFieldInstanceId = itemVal.DocTypeFieldInstanceId,
                                                            Price = 0,
                                                            RightStatus = (short)EnumJob.RightStatus.Wrong
                                                        });
                                                    }
                                                }
                                            }

                                            if (totalCorrect == 0)
                                            {
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                                rightRatio = 0;
                                            }
                                            else if (totalCorrect == itemVals.Count)
                                            {
                                                rightStatus = (short)EnumJob.RightStatus.Correct;
                                                rightRatio = 1;
                                            }
                                            else
                                            {
                                                rightRatio = (decimal)totalCorrect / (decimal)itemVals.Count;
                                                //rightStatus = (short)EnumJob.RightStatus.Confirmed;
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                            }
                                        }

                                        if (priceItems.Any())
                                        {
                                            priceDetails = JsonConvert.SerializeObject(priceItems);
                                        }

                                        // CheckFinal bị giảm 50% tổng tiền nếu QA trả lại lần LimitRoundFullMoney (Lần 2)
                                        if (itemJob.ActionCode == ActionCodeConstants.CheckFinal)
                                        {
                                            var jobWithMaxNumOfRound = allJobs
                                                .Where(x => x.ActionCode == itemJob.ActionCode && x.WorkflowStepInstanceId == itemJob.WorkflowStepInstanceId)
                                                .OrderByDescending(o => o.NumOfRound).First();
                                            if (itemJob.NumOfRound < jobWithMaxNumOfRound.NumOfRound)
                                            {
                                                // Các job CheckFinal không có NumOfRound là max thì không được tính tiền
                                                changeAmount = 0;
                                                priceDetails = null;
                                            }
                                            else
                                            {
                                                if (itemJob.NumOfRound >= LimitRoundFullMoney)
                                                {
                                                    changeAmount = Math.Round(changeAmount / 2, 2);
                                                    if (priceItems.Any())
                                                    {
                                                        priceItems.ForEach(x => x.Price = Math.Round(x.Price / 2, 2));
                                                        priceDetails = JsonConvert.SerializeObject(priceItems);
                                                    }
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
                            tempJob.PriceDetails = priceDetails;
                            tempJob.RightStatus = rightStatus;
                            tempJob.RightRatio = rightRatio;
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
            List<DocItemComplain> docItemComplains, string accessToken)
        {
            if (docItemComplains == null || !docItemComplains.Any())
            {
                Log.Logger.Error($"No change money for complain with DocInstanceId: {docInstanceId}!");
                return;
            }

            var allJobs = await _repository.GetJobByWfs(docInstanceId);
            // Chỉ lấy những jobs manual
            var allManualJobs = allJobs.Where(x => wfsInfoes.Any(w =>
                !w.IsAuto && w.InstanceId == x.WorkflowStepInstanceId &&
                (w.Attribute == (short) EnumWorkflowStep.AttributeType.File ||
                 (w.Attribute == (short) EnumWorkflowStep.AttributeType.Meta &&
                  docItemComplains.Any(d => d.DocFieldValueInstanceId == x.DocFieldValueInstanceId))))).ToList();
            var complainJobCodes = allManualJobs.Select(x => x.Code);
            var projectInstanceId = allManualJobs.FirstOrDefault()?.ProjectInstanceId;
            var clientInstanceId = await GetClientInstanceIdByProject(projectInstanceId.GetValueOrDefault(), accessToken);
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
                    short rightStatus = itemJob.RightStatus;
                    decimal rightRatio = itemJob.RightRatio;
                    string priceDetails = itemJob.PriceDetails;
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
                                ChangeProvisionalAmount = 0,
                                JobCode = itemJob.Code,
                                ProjectInstanceId = itemJob.ProjectInstanceId,
                                WorkflowInstanceId = itemJob.WorkflowInstanceId,
                                WorkflowStepInstanceId = itemJob.WorkflowStepInstanceId,
                                ActionCode = itemJob.ActionCode,
                                Message = string.Format(MsgTransactionTemplate.MsgComplainJobInfo, itemWfsInfo?.Name,
                                    itemJob.Code),
                                Description = string.Format(
                                    DescriptionTransactionTemplateV2.DescriptionTranferMoneyForComplainJob,
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
                                    var isCorrect = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemJob.Value, finalValItem);
                                    if (isCorrect)
                                    {
                                        string preVal = null;
                                        bool isPriceEdit;
                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                        {
                                            preVal = allJobs.FirstOrDefault(x =>
                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                        }
                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                        {
                                            var preVals = allJobs.FirstOrDefault(x =>
                                                x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                            var preDocItems = preVals != null && !string.IsNullOrEmpty(preVals.Value)
                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preVals.Value)
                                                : new List<DocItem>();
                                            preVal = preDocItems.FirstOrDefault(x =>
                                                x.DocTypeFieldInstanceId == itemJob.DocTypeFieldInstanceId)?.Value;
                                        }

                                        isPriceEdit = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preVal,
                                            itemJob.Value);
                                        changeAmount = MoneyHelper.GetPriceByConfigPriceV2(itemWfsInfo.ConfigPrice,
                                            itemJob.DigitizedTemplateInstanceId, itemJob.DocTypeFieldInstanceId,
                                            isPriceEdit);
                                        itemTransactionAdd.ChangeAmount = changeAmount - itemJob.Price; // = changeAmount, vì itemJob.Price = 0
                                        rightStatus = (short)EnumJob.RightStatus.Correct;
                                        rightRatio = 1;
                                        
                                        itemTransactionAdds.Add(itemTransactionAdd);
                                    }
                                    else
                                    {
                                        changeAmount = 0;   // itemJob.Price = 0
                                        rightStatus = (short)EnumJob.RightStatus.Wrong;
                                        rightRatio = 0;
                                        //itemTransactionAdd.ChangeAmount = 0;
                                    }

                                    priceDetails = JsonConvert.SerializeObject(new List<PriceItem>
                                    {
                                        new PriceItem
                                        {
                                            DocTypeFieldInstanceId = itemJob.DocTypeFieldInstanceId,
                                            Price = changeAmount,
                                            RightStatus = rightStatus
                                        }
                                    });
                                }
                                else
                                {
                                    var itemVals = JsonConvert.DeserializeObject<List<DocItem>>(itemJob.Value);
                                    if (itemVals != null && itemVals.Any())
                                    {
                                        var priceItems = new List<PriceItem>();
                                        if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByStep)
                                        {
                                            var isCorrectTotal = true;
                                            var isPriceEditTotal = true;
                                            foreach (var itemVal in itemVals)
                                            {
                                                var finalValField = docItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                if (finalValField != null)
                                                {
                                                    var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode, itemVal.Value, finalValField.Value);
                                                    if (isCorrectField)
                                                    {
                                                        Job preJob;
                                                        bool isPriceEditField;
                                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                                x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preJob?.Value, itemVal.Value);
                                                            if (!isPriceEditField)
                                                            {
                                                                isPriceEditTotal = false;
                                                            }
                                                        }
                                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                            var preDocItems = preJob != null && !string.IsNullOrEmpty(preJob.Value)
                                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preJob.Value)
                                                                : new List<DocItem>();
                                                            var preValField = preDocItems.FirstOrDefault(x =>
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
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
                                                rightRatio = 1;
                                            }
                                            else
                                            {
                                                changeAmount = 0;
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                                rightRatio = 0;
                                            }

                                            priceItems.Add(new PriceItem
                                            {
                                                DocTypeFieldInstanceId = itemJob.DocTypeFieldInstanceId,    // itemJob.DocTypeFieldInstanceId = null
                                                Price = changeAmount,
                                                RightStatus = rightStatus
                                            });
                                        }
                                        else if (objConfigPrice.Status == (short)EnumWorkflowStep.UnitPriceConfigType.ByField)
                                        {
                                            var totalCorrect = 0;
                                            foreach (var itemVal in itemVals)
                                            {
                                                var finalValField = docItems.FirstOrDefault(x => x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                if (finalValField != null)
                                                {
                                                    var isCorrectField = MoneyHelper.IsCorrect(itemWfsInfo.ActionCode,
                                                        itemVal.Value, finalValField.Value);
                                                    if (isCorrectField)
                                                    {
                                                        Job preJob;
                                                        bool isPriceEditField = true;
                                                        if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.Meta)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId &&
                                                                x.DocTypeFieldInstanceId == itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preJob?.Value, itemVal.Value);
                                                        }
                                                        else if (prevWfsInfo?.Attribute == (short)EnumWorkflowStep.AttributeType.File)
                                                        {
                                                            preJob = allJobs.FirstOrDefault(x => x.WorkflowStepInstanceId == prevWfsInfo.InstanceId);
                                                            var preDocItems = preJob != null && !string.IsNullOrEmpty(preJob.Value)
                                                                ? JsonConvert.DeserializeObject<List<DocItem>>(preJob.Value)
                                                                : new List<DocItem>();
                                                            var preValField = preDocItems.FirstOrDefault(x =>
                                                                x.DocTypeFieldInstanceId ==
                                                                itemVal.DocTypeFieldInstanceId);
                                                            isPriceEditField = MoneyHelper.IsPriceEdit(itemWfsInfo.ActionCode, preValField?.Value, itemVal.Value);
                                                        }

                                                        var changeAmountItem =
                                                            MoneyHelper.GetPriceByConfigPriceV2(
                                                                itemWfsInfo.ConfigPrice,
                                                                itemJob.DigitizedTemplateInstanceId,
                                                                itemVal.DocTypeFieldInstanceId, isPriceEditField);
                                                        changeAmount += changeAmountItem;
                                                        totalCorrect++;
                                                        priceItems.Add(new PriceItem
                                                        {
                                                            DocTypeFieldInstanceId = itemVal.DocTypeFieldInstanceId,
                                                            Price = changeAmountItem,
                                                            RightStatus = (short)EnumJob.RightStatus.Correct
                                                        });
                                                    }
                                                }
                                            }

                                            if (totalCorrect == 0)
                                            {
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                                rightRatio = 0;
                                            }
                                            else if (totalCorrect == itemVals.Count)
                                            {
                                                rightStatus = (short)EnumJob.RightStatus.Correct;
                                                rightRatio = 1;
                                            }
                                            else
                                            {
                                                rightRatio = (decimal)totalCorrect / (decimal)itemVals.Count;
                                                //rightStatus = (short)EnumJob.RightStatus.Confirmed;
                                                rightStatus = (short)EnumJob.RightStatus.Wrong;
                                            }
                                        }

                                        if (priceItems.Any())
                                        {
                                            priceDetails = JsonConvert.SerializeObject(priceItems);
                                        }

                                        // CheckFinal bị giảm 50% tổng tiền nếu QA trả lại lần LimitRoundFullMoney (Lần 2)
                                        if (itemJob.ActionCode == ActionCodeConstants.CheckFinal)
                                        {
                                            var jobWithMaxNumOfRound = allJobs
                                                .Where(x => x.ActionCode == itemJob.ActionCode && x.WorkflowStepInstanceId == itemJob.WorkflowStepInstanceId)
                                                .OrderByDescending(o => o.NumOfRound).First();
                                            if (itemJob.NumOfRound < jobWithMaxNumOfRound.NumOfRound)
                                            {
                                                // Các job CheckFinal không có NumOfRound là max thì không được tính tiền
                                                changeAmount = 0;
                                                priceDetails = null;
                                            }
                                            else
                                            {
                                                if (itemJob.NumOfRound >= LimitRoundFullMoney)
                                                {
                                                    changeAmount = Math.Round(changeAmount / 2, 2);
                                                    if (priceItems.Any())
                                                    {
                                                        priceItems.ForEach(x => x.Price = Math.Round(x.Price / 2, 2));
                                                        priceDetails = JsonConvert.SerializeObject(priceItems);
                                                    }
                                                }
                                            }
                                        }

                                        itemTransactionAdd.ChangeAmount = changeAmount - itemJob.Price;
                                        itemTransactionAdds.Add(itemTransactionAdd);
                                    }
                                }
                            }

                            // Update RightStatus & Price
                            var tempJob = (Job)itemJob.Clone();
                            tempJob.Price = changeAmount;
                            tempJob.PriceDetails = priceDetails;
                            tempJob.RightStatus = rightStatus;
                            tempJob.RightRatio = rightRatio;
                            updateJobs.Add(tempJob);
                        }
                    }
                }

                if (itemTransactionAdds.Any())
                {
                    var transactionAddMultiComplainDoc = new TransactionAddMultiDto
                    {
                        CorrelationMessage = string.Format(MsgTransactionTemplate.MsgComplainJobInfo, docInstanceId,
                            string.Join(", ", complainJobCodes)),
                        CorrelationDescription = $"Khiếu nại phiếu {docInstanceId}",
                        ItemTransactionAdds = itemTransactionAdds
                    };

                    await _transactionClientService.AddMultiTransactionAsync(transactionAddMultiComplainDoc,
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
