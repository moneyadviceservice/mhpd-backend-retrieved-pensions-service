using MhpdCommon.Constants;
using MhpdCommon.Constants.HttpClient;
using MhpdCommon.Extensions;
using MhpdCommon.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.WebJobs.Extensions.OpenApi.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using RetrievedPensionsRecordFunction.Models;
using RetrievedPensionsRecordFunction.Repository;
using System.Net;

namespace RetrievedPensionsRecordFunction
{
    public class RetrievedRecordsFunction(ILogger<RetrievedRecordsFunction> logger, IPensionRecordRepository repository, IIdValidator validator)
    {
        [Function("GetRetrievedPeis")]
        [OpenApiOperation(operationId: "get-retrieved-peis",
            Summary = "Get Retrieved Pension Records",
            Description = "Get the retrieved retrieved-pensions-records that contains pensions information has been retrieved from the PDP Ecosystem for peis.")]
        [OpenApiParameter(
            HeaderConstants.UserSessionId,
            In = ParameterLocation.Header,
            Description = "The id of the user session that the retrieved pension record is associated with.",
            Required = true)]
        [OpenApiParameter(
        HeaderConstants.CorrelationId,
        In = ParameterLocation.Header,
        Description = "An Id with which to group all logging statements made during a single session",
        Required = false)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string), 
            Description = "The array of Retrieved Peis that match the provided query parameters")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "BadRequest")]
        public async Task<IActionResult> GetPeisAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "retrieved-peis")] HttpRequest req)
        {
            return await ProcessRetrievedRecordsAsync(req, Constants.PeisGetLogSource, id => repository.GetRetrievedPeisAsync(id));
        }

        [Function("GetRetrievedRecords")]
        [OpenApiOperation(operationId: "get-retrieved-pensions-records",
            Summary = "Get Retrieved Pension Records",
            Description = "Get the retrieved retrieved-pensions-records that contains pensions information has been retrieved from the PDP Ecosystem for peis.")]
        [OpenApiParameter(
            HeaderConstants.UserSessionId,
            In = ParameterLocation.Header,
            Description = "The id of the user session that the retrieved pension record is associated with.",
            Required = true)]
        [OpenApiParameter(
        HeaderConstants.CorrelationId,
        In = ParameterLocation.Header,
        Description = "An Id with which to group all logging statements made during a single session",
        Required = false)]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.PensionCategory,
            In = ParameterLocation.Query,
            Description = "Gets a subset of the pensions retrieval records that have the specified classification.",
            Required = false)]
        [OpenApiParameter(
            QueryParams.RetrievedPensions.AssetId,
            In = ParameterLocation.Query,
            Description = "Gets a specific pensions retrieval record with the specified Id.",
            Required = false)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(string),
            Description = "The array of Retrieved Pension Records that match the provided query parameters")]
        [OpenApiResponseWithoutBody(HttpStatusCode.BadRequest, Description = "BadRequest")]
        public async Task<IActionResult> GetAsync([HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "retrieved-pension-records")] HttpRequest req)
        {
            var category = req.Query[QueryParams.RetrievedPensions.PensionCategory];
            var assetId = req.Query[QueryParams.RetrievedPensions.AssetId];

            return await ProcessRetrievedRecordsAsync(req, Constants.PensionsGetLogSource, id => repository.GetRetrievedRecordsAsync(id, category, assetId));
        }

        [Function("DeleteRetrievedRecords")]
        [OpenApiOperation(operationId: "delete-pensions-retrieved-records-id",
            Summary = "Delete Pensions Retrieved Record",
            Description = "Deletes the given pension retrieved record id.")]
        [OpenApiParameter(
            HeaderConstants.UserSessionId,
            In = ParameterLocation.Header,
            Description = "The id of the user session that the retrieved pension record is associated with.",
            Required = true)]
        [OpenApiParameter(
        HeaderConstants.CorrelationId,
        In = ParameterLocation.Header,
        Description = "An Id with which to group all logging statements made during a single session",
        Required = false)]
        [OpenApiResponseWithBody(HttpStatusCode.OK, contentType: "application/json", bodyType: typeof(int),
            Description = "The number of records deleted as part of the request")]
        public async Task<IActionResult> DeleteAsync([HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "retrieved-pension-records")] HttpRequest req)
        {
            return await ProcessRetrievedRecordsAsync(req, Constants.PensionsDeleteLogSource, repository.DeleteRetrievedRecordsAsync);
        }

        private async Task<IActionResult> ProcessRetrievedRecordsAsync<T>(HttpRequest req, string logSource, Func<string, Task<T>> processor)
        {
            var correlationId = req.Headers[HeaderConstants.CorrelationId].ToString();

            if (string.IsNullOrEmpty(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
            }

            if (!validator.IsValidGuid(correlationId))
            {
                return new BadRequestObjectResult(Constants.InvalidCorrelationId);
            }

            using var scope = logger.BeginCorrelationScope(correlationId, logSource);

            var userSessionId = req.Headers[HeaderConstants.UserSessionId].ToString();

            logger.LogRequest($"User Session Id: {userSessionId}");

            if (!validator.IsValidGuid(userSessionId))
            {
                logger.LogError("Unable to service request for session [{SessionId}]: {Reason}", userSessionId, Constants.InvalidSessionId);
                return new BadRequestObjectResult(Constants.InvalidSessionId);
            }

            var records = await processor(userSessionId);

            logger.LogResponse(records);

            return new OkObjectResult(records);
        }
    }
}
