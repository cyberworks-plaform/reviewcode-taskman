﻿using Axe.TaskManagement.Service.Dtos;
using Axe.TaskManagement.Service.Services.Interfaces;
using Axe.Utility.EntityExtensions;
using Ce.Constant.Lib.Definitions;
using Ce.Constant.Lib.Dtos;
using Ce.Interaction.Lib.HttpClientAccessors.Interfaces;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Axe.TaskManagement.Service.Services.Implementations
{
    public class DocTypeFieldClientService : IDocTypeFieldClientService
    {
        private readonly IBaseHttpClientFactory _clientFatory;
        private readonly string _serviceUri;

        public DocTypeFieldClientService(IBaseHttpClientFactory clientFatory)
        {
            _clientFatory = clientFatory;
            _serviceUri = $"{ApiDomain.AxeCoreEndpoint}/doctypefield";
        }

        public async Task<GenericResponse<List<DocTypeFieldDto>>> GetByProjectAndDigitizedTemplateInstanceId(Guid projectInstanceId, Guid digitizedTemplateInstanceId, string accessToken)
        {
            GenericResponse<List<DocTypeFieldDto>> response;
            try
            {
                var client = _clientFatory.Create();
                var requestParam = new Dictionary<string, string>
                {
                    { "projectInstanceId", projectInstanceId.ToString() },
                    { "digitizedTemplateInstanceId", digitizedTemplateInstanceId.ToString() }
                };
                var apiEndpoint = "get-by-digitized-template-project-instanceId";
                response = await client.GetAsync<GenericResponse<List<DocTypeFieldDto>>>(_serviceUri, apiEndpoint, requestParam, null, accessToken);
                if (!response.Success)
                {
                    Serilog.Log.Error(response.Message);
                    Serilog.Log.Error(response.Error);
                }
            }
            catch (Exception ex)
            {
                response = GenericResponse<List<DocTypeFieldDto>>.ResultWithError((int)HttpStatusCode.BadRequest, ex.StackTrace, ex.Message);
                Serilog.Log.Error(ex, ex.Message);
            }

            return response;
        }
        public List<DocItem> ConvertToDocItem(List<DocTypeFieldDto> listDocTypeField)
        {
            List<DocItem> listDocItem = new List<DocItem>();
            foreach (var docTypeField in listDocTypeField)
            {
                var docItem = new DocItem()
                {
                    DocTypeFieldInstanceId = docTypeField.InstanceId,
                    DocTypeFieldId = docTypeField.Id,
                    DocTypeFieldCode = docTypeField.Code,
                    DocTypeFieldName = docTypeField.Name,
                    DocTypeFieldSortOrder = docTypeField.SortOrder.GetValueOrDefault(),
                    InputType = docTypeField.InputType,
                    MaxLength = docTypeField.MaxLength,
                    MinLength = docTypeField.MinLength,
                    MaxValue = docTypeField.MaxValue,
                    MinValue = docTypeField.MinValue,
                    PrivateCategoryInstanceId = docTypeField.PrivateCategoryInstanceId,
                    IsMultipleSelection = docTypeField.IsMultipleSelection,
                    CoordinateArea = docTypeField.CoordinateArea,
                    ShowForInput = docTypeField.ShowForInput,
                    Format = docTypeField.Format,
                    InputShortNote = docTypeField.InputShortNote,
                };
                listDocItem.Add(docItem);
            }
            return listDocItem;
        }
    }
}
