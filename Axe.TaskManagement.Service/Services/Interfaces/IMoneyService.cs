using Axe.TaskManagement.Model.Entities;
using Axe.Utility.EntityExtensions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IMoneyService
{
    public Task ChargeMoneyForCompleteDoc(List<WorkflowStepInfo> wfsInfoes,
        List<WorkflowSchemaConditionInfo> wfSchemaInfoes, List<DocItem> docItems, Guid docInstanceId,
        string accessToken);

    public Task ChargeMoneyForComplainJob(List<WorkflowStepInfo> wfsInfoes,
        List<WorkflowSchemaConditionInfo> wfSchemaInfoes, List<DocItem> docItems, Guid docInstanceId,
        List<DocItemComplain> docItemComplains, string accessToken);
}