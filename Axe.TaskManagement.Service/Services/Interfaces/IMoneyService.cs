using Axe.Utility.EntityExtensions;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using Axe.TaskManagement.Service.Dtos;

namespace Axe.TaskManagement.Service.Services.Interfaces;

public interface IMoneyService
{
    public Task ChargeMoneyForCompleteDoc(List<WorkflowStepInfo> wfsInfoes,
        List<WorkflowSchemaConditionInfo> wfSchemaInfoes, List<DocItem> docItems, Guid docInstanceId,
        string accessToken);

    public Task ChargeMoneyForCompleteDocByField(List<WorkflowStepInfo> wfsInfoes,
        List<WorkflowSchemaConditionInfo> wfSchemaInfoes, List<DocItem> docItems, Guid docInstanceId, List<Guid?> fields,
        string accessToken);
}